using AdventurePacks.Api.Configuration.Options;

namespace AdventurePacks.Api.Services.Beki;

/// <summary>
/// The single reason a photo was refused, and the wording that goes with it.
///
/// A code rather than free text because the browser has to say this in whichever language the
/// parent is reading, and a sentence written by the model can only ever be in one. The Georgian
/// wording travels with the response anyway, so a caller that has no copy of its own — a future
/// client, a log, a support conversation — still gets something a person can read.
/// </summary>
public static class PortraitGateReasons
{
    public const string Ok = "ok";

    // Returned by the model, one per refusal.
    public const string NotAPerson = "not_a_person";
    public const string NoFace = "no_face";
    public const string MultiplePeople = "multiple_people";
    public const string FaceObscured = "face_obscured";
    public const string FaceTooSmall = "face_too_small";
    public const string TooDark = "too_dark";

    // Decided here rather than by the model.
    public const string Unsuitable = "unsuitable";
    public const string Unreadable = "unreadable";
    public const string TooLarge = "too_large";
    public const string Unavailable = "unavailable";

    private static readonly Dictionary<string, string> Messages = new(StringComparer.OrdinalIgnoreCase)
    {
        [NotAPerson] = "ეს ბავშვის ფოტო არ არის — ატვირთე სურათი, სადაც ბავშვის სახე ჩანს.",
        [NoFace] = "სახე არ ჩანს — ატვირთე ფოტო, სადაც ბავშვი კამერას უყურებს.",
        [MultiplePeople] = "ფოტოზე რამდენიმე ადამიანია — ატვირთე სურათი მხოლოდ ერთი ბავშვით.",
        [FaceObscured] = "სახე დაფარული ან ბუნდოვანია — ატვირთე მკაფიო ფოტო სათვალის ან ნიღბის გარეშე.",
        [FaceTooSmall] = "ბავშვი ძალიან შორსაა — ატვირთე ფოტო, სადაც სახე კადრს ავსებს.",
        [TooDark] = "ფოტო ძალიან ბნელია — ატვირთე კარგად განათებული სურათი.",
        [Unsuitable] = "ეს ფოტო არ გამოდგება — ატვირთე მკაფიო პორტრეტი, სადაც ბავშვის სახე კარგად ჩანს.",
        [Unreadable] = "ფაილი ვერ წავიკითხეთ — ატვირთე JPG, PNG ან WEBP ფოტო.",
        [TooLarge] = "ფოტო ძალიან დიდია — აირჩიე უფრო პატარა სურათი.",
        [Unavailable] = "ფოტოს შემოწმება ვერ მოხერხდა — სცადე ხელახლა ატვირთვა.",
    };

    /// <summary>True for a code the model is allowed to return as a refusal.</summary>
    public static bool IsModelReason(string? reason) =>
        reason is NotAPerson or NoFace or MultiplePeople or FaceObscured or FaceTooSmall or TooDark;

    public static string MessageFor(string reason) =>
        Messages.TryGetValue(reason, out var message) ? message : Messages[Unsuitable];
}

/// <summary>
/// What the browser gets back. <c>Explanation</c> is deliberately absent: it describes the photo
/// the model was shown, and a description of somebody's child is not something to hand back over
/// an anonymous endpoint. It goes to the log instead.
/// </summary>
public sealed record PortraitVerdict(bool Accepted, string Reason, string Message)
{
    public static PortraitVerdict Pass() => new(true, PortraitGateReasons.Ok, string.Empty);

    public static PortraitVerdict Fail(string reason) =>
        new(false, reason, PortraitGateReasons.MessageFor(reason));
}

public interface IPortraitGate
{
    /// <summary>
    /// Decides whether a photo can stand in for the book's hero. Never throws for a photo it
    /// dislikes or a model it could not reach — every outcome is a verdict, because the caller
    /// has exactly one thing to do with any of them.
    /// </summary>
    Task<PortraitVerdict> InspectAsync(byte[] photoBytes, string contentType, CancellationToken cancellationToken);
}

/// <summary>
/// Asks the vision model one question about a chosen photo: is this a real person's face?
///
/// It exists because nothing else in the pipeline ever asks. The identity analyzer is handed the
/// photo and told to extract a face from it, so shown a bottle it dutifully describes a bottle,
/// and the book is written, illustrated and paid for around it. Checking costs one small call at
/// the moment the file is picked, which is the only moment the answer is still cheap and the only
/// moment a parent can do anything about it.
///
/// It fails closed, and says so in its own words.
///
/// This was briefly the other way round, to get past a gate that refused everything because it
/// was configured with a model name nothing had deployed. That was the right diagnosis and the
/// wrong remedy: with the model fixed, letting photos through whenever the check could not run
/// meant an object could still reach a finished book, silently, and the only sign was a parent
/// being told their drawing was "ready".
///
/// So a check that cannot run is a refusal — but a distinct one. "We could not check this, try
/// again" is a different sentence from "this is not a photo of a child", and the parent whose
/// photo was fine is told the truth rather than being blamed for an outage.
/// </summary>
public sealed class PortraitGate(
    IBekiOpenAiClient client,
    IBekiPromptProvider prompts,
    IOptions<BekiOptions> options,
    IOptions<OpenAiOptions> openAiOptions,
    ILogger<PortraitGate> logger) : IPortraitGate
{
    /// <summary>
    /// The browser downscales to a 1024px JPEG before sending, so anything near this is a client
    /// that did not — an old cached bundle, or a script. Refusing here keeps an oversized image
    /// from being base64'd into a model request.
    /// </summary>
    public const int MaxPhotoBytes = 8_000_000;

    private readonly BekiOptions _beki = options.Value;
    private readonly OpenAiOptions _openAi = openAiOptions.Value;

    /// <summary>
    /// The configured gate model, or the account's ordinary vision model when none is set.
    /// A gate that cannot name a model it can actually reach refuses every photo.
    /// </summary>
    private string GateModel =>
        string.IsNullOrWhiteSpace(_beki.PortraitGateModel) ? _openAi.Model : _beki.PortraitGateModel;

    public async Task<PortraitVerdict> InspectAsync(
        byte[] photoBytes,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (photoBytes.Length == 0)
        {
            return PortraitVerdict.Fail(PortraitGateReasons.Unreadable);
        }

        if (photoBytes.Length > MaxPhotoBytes)
        {
            return PortraitVerdict.Fail(PortraitGateReasons.TooLarge);
        }

        // A parent is watching a spinner on a form. The generation calls run for minutes under a
        // background job; this one gets its own short leash regardless of how they are configured.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(3, _beki.PortraitGateTimeoutSeconds)));

        PortraitGateResponse? response;
        try
        {
            response = await client.CompleteJsonWithImagesAsync<PortraitGateResponse>(
                GateModel,
                prompts.Get(BekiPromptProvider.PortraitGate),
                new { task = "portrait_intake_check" },
                [new BekiImageAttachment("Chosen photo", photoBytes, contentType)],
                timeout.Token,
                prompts.GetSchema(BekiPromptProvider.PortraitGateSchema));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The parent navigated away or the request was aborted; nobody is left to answer.
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Portrait gate timed out after {Seconds}s; refusing as unchecked.",
                _beki.PortraitGateTimeoutSeconds);
            return PortraitVerdict.Fail(PortraitGateReasons.Unavailable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Portrait gate could not reach the vision model; refusing as unchecked.");
            return PortraitVerdict.Fail(PortraitGateReasons.Unavailable);
        }

        return Interpret(response, logger);
    }

    /// <summary>
    /// Turns whatever the model said into a verdict. Separate and static so the rules can be read
    /// and tested without a model: an unparsable answer is not an accepted photo, and a refusal
    /// with a code we do not recognise still has to reach the parent as something actionable.
    /// </summary>
    internal static PortraitVerdict Interpret(PortraitGateResponse? response, ILogger logger)
    {
        if (response is null)
        {
            logger.LogWarning("Portrait gate returned nothing parsable; refusing as unchecked.");
            return PortraitVerdict.Fail(PortraitGateReasons.Unavailable);
        }

        if (response.Accepted)
        {
            // Logged as well as the refusals. When a photo gets through that should not have,
            // the model's own description of what it saw is the only way to tell whether the
            // prompt was too generous or the picture genuinely looked like a child.
            logger.LogInformation("Portrait gate accepted a photo: {Explanation}", response.Explanation);
            return PortraitVerdict.Pass();
        }

        logger.LogInformation("Portrait gate refused a photo: {Reason} — {Explanation}",
            response.Reason, response.Explanation);

        return PortraitVerdict.Fail(
            PortraitGateReasons.IsModelReason(response.Reason)
                ? response.Reason!
                : PortraitGateReasons.Unsuitable);
    }
}

/// <summary>The model's answer, shaped by <c>portrait-gate-v1.schema.json</c>.</summary>
public sealed class PortraitGateResponse
{
    public bool Accepted { get; set; }
    public string? Reason { get; set; }
    public string? Explanation { get; set; }
}
