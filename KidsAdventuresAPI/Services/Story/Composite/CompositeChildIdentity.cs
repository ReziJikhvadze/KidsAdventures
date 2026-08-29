using System.Globalization;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Infrastructure;
using Json.Schema;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// The four attributes that make the child on page seven the child on page one.
///
/// Four and no more, and each of them short. The point of the spec is to be repeated verbatim in
/// nine image prompts, so anything longer than a phrase is a paragraph the model will paraphrase
/// differently each time — which is the drift this exists to stop, arriving by another route. And
/// anything outside these four is either the outfit (the Visual Scenario's job), the pose (the
/// scene's), or something nobody should be deriving from a child's photograph at all.
/// </summary>
public sealed record ChildIdentitySpec
{
    public required string HairColor { get; init; }

    public required string HairStyle { get; init; }

    public required string EyeColor { get; init; }

    public required string SkinTone { get; init; }
}

/// <summary>Either a spec, or the reasons an answer was not one. Never a half-read spec.</summary>
/// <param name="Problems">
/// Value-free by construction. These strings are appended to the corrective retry AND recorded as
/// the call's validation result, and the validation result goes to a log — so a problem that
/// quoted the offending value would put an attribute of a real child in a log line, which is
/// exactly what this whole component is careful not to do.
/// </param>
public sealed record ChildIdentityParseResult(
    bool IsValid, ChildIdentitySpec? Spec, IReadOnlyList<string> Problems)
{
    public string Summary => string.Join("; ", Problems);
}

/// <summary>
/// The per-book child identity spec: how it is asked for, how an answer is read, how it is written
/// into an image prompt, and how it is stored.
///
/// It exists because the first real composite book drifted. Identity rode entirely on the attached
/// photograph, which means every spread was an independent stylization of the same photo — nine
/// interpretations, each individually defensible, of a child whose hair the reader can see changing
/// between two pages that face each other. The image prompt carried no identity attributes at all
/// (the planner is forbidden to invent them, correctly), and the one attached image that shows a
/// drawn child — the continuity reference — tells the model not to copy the child.
///
/// So the attributes are derived once, from the photograph, and repeated on every page. The
/// photograph stays the identity authority in the prompt's own words: the list is what keeps nine
/// readings of it consistent, not a replacement for it.
///
/// Two rules run through everything below.
///
/// The spec is private. It is derived from a consented photograph and describes a real child's
/// body, so it lives where the photograph lives — the pack's own storage — and it never reaches a
/// log, a telemetry document or an exception message. What is logged is that a spec was derived or
/// adopted and which prompt version did it. Not a digest of it either: see the note above
/// <see cref="ToStoredJson"/> for why a hash of four low-entropy attributes is not a safe stand-in
/// for them.
///
/// The spec is required. A book that cannot derive one stops with
/// <see cref="CompositeFailureCodes.IdentitySpecFailed"/> rather than drawing without it: the
/// drifting book passed every one of its own QA checks, so "carry on and let review catch it" is a
/// policy that has already been tried and has already failed.
/// </summary>
public static class CompositeChildIdentity
{
    /// <summary>The derivation prompt's version, recorded against the call and the stored spec.</summary>
    public const string Version = "child-identity-spec-v1.1";

    /// <summary>The longest a single attribute may be. A sentence is not an attribute.</summary>
    public const int MaxAttributeLength = 60;

    private static readonly Lazy<JsonSchema> Schema = new(BuildSchema);

    private static readonly JsonSerializerOptions StoredJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // -----------------------------------------------------------------------------------------
    // The call
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// What the vision model is asked, once per book.
    ///
    /// Written so that the answer is usable as prompt text without editing: short neutral phrases
    /// an illustrator could act on. The prohibitions are not decoration either — a model asked to
    /// describe a child's photograph will volunteer an ethnicity, a mood or a guess at a name
    /// unless it is told not to, and none of those may enter a prompt, a log or a stored document.
    /// </summary>
    public const string Prompt =
        """
        You are the identity reader for BEKI personalized children's books.

        The attached image is one consented photograph of the child this book is being made for. Read only the four physical attributes listed below, so that the illustrator can draw the same child on every page of the book.

        Return valid JSON only, with exactly this structure and no additional keys:

        {
          "hair_color": "short plain phrase",
          "hair_style": "short plain phrase",
          "eye_color": "one or two plain words",
          "skin_tone": "short plain phrase"
        }

        Rules:

        - Every value is a short, plain, neutral description of what is visible: at most six words, not a sentence, with no trailing punctuation.
        - Describe hair colour and hair style the way an illustrator would need them, for example "dark brown" and "shoulder-length wavy with a fringe".
        - Give the eye colour as one or two plain words.
        - Describe skin tone in plain, neutral illustration terms, for example "light warm", "medium olive", or "deep brown".
        - Do not state or guess the child's name, ethnicity, nationality, religion, health, or mood.
        - Do not describe clothing, accessories, background, expression, or anything that is not one of the four attributes.
        - Do not add commentary, caveats, or explanation. Return the JSON object and nothing else.
        """;

    /// <summary>
    /// The one permitted corrective retry: the same ask, whole, with what was wrong appended.
    ///
    /// Appended rather than rewritten, the same idiom the scenario retry uses — a different
    /// instruction returns a different answer, and what is wanted is this answer without the fault.
    /// </summary>
    public static string RetryPrompt(IReadOnlyList<string> problems) =>
        Prompt
        + "\n\nThe previous answer could not be used: "
        + string.Join("; ", problems)
        + " Return only the JSON object described above.";

    // -----------------------------------------------------------------------------------------
    // Reading an answer
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Reads one answer, forgiving about the wrapper and strict about the content — the same trade
    /// <see cref="CompositeMinimalQa.Parse"/> makes, for the same reason: a fenced JSON object is
    /// not a failed answer, and a fifth key or an empty value is.
    /// </summary>
    public static ChildIdentityParseResult Parse(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return Invalid("the identity call returned no text.");
        }

        var json = ModelJsonSanitizer.ExtractJsonObject(answer);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Invalid("the identity answer contains no JSON object.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // The message, deliberately not included: a parser error quotes the text it choked on.
            return Invalid("the identity answer is not valid JSON.");
        }

        using (document)
        {
            var results = Schema.Value.Evaluate(
                document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

            if (!results.IsValid)
            {
                // The instance location and the keyword only — never the error text, which
                // JsonSchema.Net fills with the offending value for length and type failures.
                var details = (results.Details ?? [])
                    .Where(detail => !detail.IsValid && detail.Errors is { Count: > 0 })
                    .SelectMany(detail => detail.Errors!.Select(error =>
                        $"{Location(detail.InstanceLocation.ToString())} failed '{error.Key}'"))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                return new ChildIdentityParseResult(
                    false,
                    null,
                    details.Count > 0
                        ? details
                        : ["the identity answer does not have the four required attributes."]);
            }

            var root = document.RootElement;

            var spec = new ChildIdentitySpec
            {
                HairColor = Tidy(root.GetProperty("hair_color").GetString()!),
                HairStyle = Tidy(root.GetProperty("hair_style").GetString()!),
                EyeColor = Tidy(root.GetProperty("eye_color").GetString()!),
                SkinTone = Tidy(root.GetProperty("skin_tone").GetString()!),
            };

            // Tidying can empty a value the schema accepted — a string of spaces and a full stop
            // satisfies minLength and is not an attribute.
            var emptied = new[]
                {
                    ("hair_color", spec.HairColor), ("hair_style", spec.HairStyle),
                    ("eye_color", spec.EyeColor), ("skin_tone", spec.SkinTone)
                }
                .Where(pair => pair.Item2.Length == 0)
                .Select(pair => $"{pair.Item1} is empty once punctuation and spacing are removed.")
                .ToList();

            return emptied.Count > 0
                ? new ChildIdentityParseResult(false, null, emptied)
                : new ChildIdentityParseResult(true, spec, []);
        }
    }

    /// <summary>
    /// The parent's own answer wins over the model's reading of the photograph.
    ///
    /// The application has asked parents for an eye colour since long before this pipeline existed,
    /// and a parent looking at their child is a better source than a model looking at a photograph
    /// where the eyes may be forty pixels wide. Only this one attribute: it is the only one the
    /// form collects, and it is the one the drifting book lost outright.
    ///
    /// An unusable value — absent, blank, or long enough to be prose rather than a colour — leaves
    /// the derived value alone. Nothing about which is worth failing a book over.
    /// </summary>
    public static ChildIdentitySpec WithParentEyeColor(ChildIdentitySpec spec, string? parentEyeColor)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var stated = Tidy(parentEyeColor ?? string.Empty);

        return stated.Length is 0 or > MaxAttributeLength
            ? spec
            : spec with { EyeColor = stated };
    }

    // -----------------------------------------------------------------------------------------
    // Using it
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The CHILD IDENTITY LOCK block, as the v1.1 template writes it.
    ///
    /// The last line is load-bearing and is the reason this block is safe to add at all: the
    /// photograph stays the authority. Four phrases cannot describe a face, and a prompt that
    /// presented them as the specification rather than as the consistency rule would trade one
    /// failure — a child who changes between pages — for a worse one, a child who is nobody in
    /// particular on all eight.
    /// </summary>
    public static string LockBlock(ChildIdentitySpec spec, int childAge)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return $"""
            CHILD IDENTITY LOCK
            Hair colour: {spec.HairColor}
            Hair style: {spec.HairStyle}
            Eye colour: {spec.EyeColor}
            Skin tone: {spec.SkinTone}
            The child is approximately {childAge.ToString(CultureInfo.InvariantCulture)} years old.
            These attributes are identical on the cover and on all eight spreads. Image 1 remains the identity authority; where this list and Image 1 disagree, follow Image 1.
            """;
    }

    /*
      There is deliberately no digest of the spec anywhere in this class.

      An earlier version logged one — a SHA-256 over the four attributes, salted with the job id —
      so that an operator could tell whether a resumed run had adopted the same spec as the attempt
      before it. It looked safe and was not. The attribute vocabulary is tiny: a few dozen plausible
      hair colours, a handful of eye colours, a short list of skin tones and hair styles, which is a
      space small enough to enumerate exhaustively on a laptop. The salt was the job id, and the job
      id is logged on the very same line — so anybody who could read the log could read the salt,
      grind the space and recover the child's attributes exactly. A hash whose input space is
      enumerable and whose salt is public is an encoding, not a protection.

      What the log carries instead is that a spec was derived or adopted and which prompt version
      did it. The question the digest was there to answer — did this attempt reuse the earlier
      spec? — is answered by "identity_spec_adopted" appearing at all, which is the same answer
      without the child in it. A real fingerprint would need an HMAC under a server-side key that
      log readers do not have, and nothing in this pipeline needs one badly enough to introduce key
      management for it.
    */

    // -----------------------------------------------------------------------------------------
    // Persisting it
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The spec as it is stored beside the book's other private artifacts, carrying the version of
    /// the prompt that derived it.
    ///
    /// The version travels with the document rather than only in the resume contract because the
    /// two answer different questions: the contract decides whether this book may be resumed at
    /// all, and this field decides whether the bytes in front of a resumed run are a spec it may
    /// adopt. A stored spec from an older derivation prompt is not a corrupt file — it is a
    /// perfectly good answer to a different question — and the honest response is to derive again.
    /// </summary>
    public static string ToStoredJson(ChildIdentitySpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return JsonSerializer.Serialize(
            new
            {
                derivation_version = Version,
                hair_color = spec.HairColor,
                hair_style = spec.HairStyle,
                eye_color = spec.EyeColor,
                skin_tone = spec.SkinTone,
            },
            StoredJson);
    }

    /// <summary>
    /// The stored spec, when there is one and this deployment may still use it.
    ///
    /// Null for anything else — absent, unreadable, incomplete, or derived by a different prompt
    /// version — and null means "derive a new one", which is also what a first attempt does. It
    /// never throws: a resumed job must not die over a file it can simply replace.
    /// </summary>
    public static ChildIdentitySpec? TryReadStored(string? storedJson)
    {
        if (string.IsNullOrWhiteSpace(storedJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(storedJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("derivation_version", out var version)
                || !string.Equals(version.GetString(), Version, StringComparison.Ordinal))
            {
                return null;
            }

            // The four attributes on their own, because the stored document deliberately carries a
            // fifth field and the schema deliberately rejects a fifth field. Re-reading the whole
            // document through Parse would reject every spec this class has ever written.
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var name in (string[])["hair_color", "hair_style", "eye_color", "skin_tone"])
            {
                if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                attributes[name] = value.GetString()!;
            }

            var parsed = Parse(JsonSerializer.Serialize(attributes));
            return parsed.IsValid ? parsed.Spec : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // -----------------------------------------------------------------------------------------

    private static ChildIdentityParseResult Invalid(string problem) => new(false, null, [problem]);

    private static string Location(string location) =>
        location.Length == 0 ? "(root)" : location.TrimStart('/');

    /// <summary>
    /// One line, no double spaces, no trailing punctuation — the shape the prompt asks for, applied
    /// rather than assumed. A value that arrives as "Dark brown." reaches nine image prompts, and
    /// the difference between an attribute and a sentence fragment is visible in the output.
    /// </summary>
    private static string Tidy(string value)
    {
        var builder = new StringBuilder(value.Length);
        var space = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        return builder.ToString().Trim('.', ',', ';', ':', '"', '\'', ' ');
    }

    /// <summary>
    /// The shape an answer has to have. Written here rather than shipped as a contract file
    /// because it is this campaign's addition rather than the supplier's document — the supplied
    /// contracts folder stays what the supplier delivered, amended only where they are amended.
    ///
    /// The evaluation is local, so it may use the keywords a provider's strict structured-output
    /// mode rejects: nothing about this schema goes on the wire.
    /// </summary>
    private static JsonSchema BuildSchema() =>
        JsonSchema.FromText(
            $$"""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "additionalProperties": false,
              "required": ["hair_color", "hair_style", "eye_color", "skin_tone"],
              "properties": {
                "hair_color": {"type": "string", "minLength": 1, "maxLength": {{MaxAttributeLength}}},
                "hair_style": {"type": "string", "minLength": 1, "maxLength": {{MaxAttributeLength}}},
                "eye_color": {"type": "string", "minLength": 1, "maxLength": {{MaxAttributeLength}}},
                "skin_tone": {"type": "string", "minLength": 1, "maxLength": {{MaxAttributeLength}}}
              }
            }
            """);
}
