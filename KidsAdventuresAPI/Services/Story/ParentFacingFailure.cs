using AdventurePacks.Api.Services.Story.Composite;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// The translation between what a failed book records and what its parent is told.
///
/// Two audiences read the same failure, and they need opposite things from it. An operator needs
/// the code and the spread number — <c>IMAGE_QA_FAILED (spread 1): the child's hair does not
/// match</c> — because that is what says which stage to look at. A parent who paid 79 GEL needs a
/// sentence in their own language that says the book did not get made, that somebody knows, and
/// that they are not being asked to do anything about it. Handing them the operator's string is
/// what the product did until now: an English error code, on a screen that had just been promising
/// them a picture book, about a child's photograph.
///
/// So the code never leaves the building. Every parent-facing surface — the order status the
/// generating screen polls, the book on the shelf, the email — takes its words from here, and the
/// raw message stays where it is useful: the row, the logs, the admin order page and the alert to
/// whoever is on duty.
///
/// The mapping is deliberately coarse. Nine failure codes do not need nine apologies, and a parent
/// cannot act on the difference between a refused illustration and an unusable identity read. They
/// are grouped by the only distinction that changes what the reader should expect: whether the
/// book stopped on its own quality bar, on its words, on its pages, or simply ran out of time.
/// Every group ends the same way, because every group has the same ending — somebody is already
/// looking at it.
/// </summary>
public static class ParentFacingFailure
{
    /// <summary>
    /// What a failed book's progress line says once nothing is running any more.
    ///
    /// The legacy pipeline has written exactly this sentence over the frozen mid-generation line
    /// since generation failures were first handled, and the Beki path had no equivalent: it wrote
    /// the status and left "იხატება მე-2 გვერდი — 18%" standing forever. Both now write this one,
    /// from here, so the two pipelines cannot drift into two different apologies.
    ///
    /// Shorter and blunter than the messages below on purpose: it is a progress line under a bar
    /// that has stopped, not the explanation. The explanation is
    /// <see cref="ToParentMessage"/>.
    /// </summary>
    public const string ProgressLine = "რაღაც შეფერხდა. სცადე ხელახლა ან აირჩიე სხვა თემა.";

    /// <summary>The ending every message shares: somebody knows, and nothing is being asked of you.</summary>
    private const string Reassurance = "ჩვენ უკვე ვმუშაობთ ამაზე და მალე დაგიკავშირდებით.";

    /// <summary>
    /// The parent's version of one stored failure message.
    ///
    /// Never null and never empty, whatever it is handed — including null, an empty string, or a
    /// message with no recognisable code in front of it. A caller reaching for this has already
    /// decided the parent is being told something; returning nothing would leave the screen blank
    /// at precisely the moment the parent needs a sentence.
    /// </summary>
    public static string ToParentMessage(string? errorMessage) => LeadingCode(errorMessage) switch
    {
        // The book stopped on its own quality bar — the pictures, the child's likeness, or the
        // plan the pictures are drawn from. One sentence for all of them: "the drawing did not
        // pass our own check" is the whole of what a parent can usefully know.
        CompositeFailureCodes.ImageQaFailed
            or CompositeFailureCodes.ImageGenerationFailed
            or CompositeFailureCodes.IdentitySpecFailed
            or CompositeFailureCodes.VisualScenarioFailed =>
            "წიგნის ხატვა ვერ დასრულდა — სურათებმა ჩვენი ხარისხის შემოწმება ვერ გაიარეს. "
            + Reassurance,

        // The words, or the details the words were to be built from. Nothing was drawn at all.
        CompositeFailureCodes.StoryFailed or CompositeFailureCodes.InvalidBookInput =>
            "წიგნის ტექსტის მომზადება ვერ დასრულდა. " + Reassurance,

        // The book exists as pictures and words but could not be laid out or prepared for print.
        //
        // ASSET_LOCK_FAILED belongs here rather than with the quality refusals, and the reason is
        // what the parent would otherwise be told. It is a fault in OUR fixed materials — a licensed
        // font, the approved Beki artwork, the printer's colour profile — found before a single
        // image is paid for, so nothing about this book was ever drawn or refused. Reading the
        // generic arm's "the drawing did not pass our own check" would tell a family their child's
        // book failed a quality bar it never reached. It is a production fault, and the closest true
        // sentence is the one about preparing the book.
        CompositeFailureCodes.TextOverflow
            or CompositeFailureCodes.LayoutFailed
            or CompositeFailureCodes.PrintPreflightFailed
            or BekiAssetLock.FailureCode =>
            "წიგნის გვერდების დალაგება ვერ დასრულდა. " + Reassurance,

        // Not a fault in the book: the machinery was too slow or stopped answering. Worth its own
        // wording, because "it took too long" is the one failure a parent might otherwise blame
        // their own connection for.
        GenerationBudget.ExceededCode or GenerationBudget.StalledCode or TimedOutCode =>
            "წიგნის შექმნას ჩვეულებრივზე მეტი დრო დასჭირდა და შეწყდა. " + Reassurance,

        // Anything else, including a code minted after this file was written. The default is the
        // honest one rather than a guess at which group a new code belongs to.
        _ => "წიგნის ხატვა ვერ დასრულდა — " + Reassurance
    };

    /// <summary>
    /// A synonym for the budget's own code that has appeared in stored messages.
    ///
    /// Kept as a literal rather than added to <see cref="GenerationBudget"/>: nothing writes it any
    /// more, and promoting it to a constant beside <see cref="GenerationBudget.ExceededCode"/>
    /// would invite something to start.
    /// </summary>
    private const string TimedOutCode = "GENERATION_TIMED_OUT";

    /// <summary>
    /// The code word at the front of a stored message, or an empty string when there is not one.
    ///
    /// Every writer uses the same shape — <c>CODE: sentence</c>, or <c>CODE (spread 4): sentence</c>
    /// — so the code is the leading run of capitals, digits and underscores. Read that way rather
    /// than by splitting on a separator, because the separator differs between the two shapes, and
    /// a message with no code at all (an exception's own text, say) must fall through to the
    /// default instead of matching its first word.
    /// </summary>
    private static string LeadingCode(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return string.Empty;
        }

        var text = errorMessage.AsSpan().TrimStart();
        var length = 0;
        while (length < text.Length && (char.IsAsciiLetterUpper(text[length])
                                        || char.IsAsciiDigit(text[length])
                                        || text[length] == '_'))
        {
            length++;
        }

        // A single capital is the start of an ordinary sentence, not a code.
        return length < 2 ? string.Empty : text[..length].ToString();
    }
}
