namespace AdventurePacks.Api.DTOs.Admin;

public sealed class AdminOrderListResponse
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<AdminOrderRow> Items { get; set; } = [];
}

/// <summary>
/// One line of the order list.
///
/// The four fields after <see cref="FulfilledAt"/> are not about the order at all — they
/// describe the book it bought, and they are here because the question the list is opened to
/// answer is "did this customer actually get anything". Carrying them on the row costs one
/// more join and saves opening every order to find the one that went wrong.
/// </summary>
public sealed class AdminOrderRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public Guid? BookId { get; set; }
    public string? BookTitle { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Package { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = GelPricing.Currency;
    public int SubtotalMinor { get; set; }
    public int DiscountMinor { get; set; }
    public int TotalMinor { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>Which gateway took the money: "Bog", "Stripe", or "Promo" for a free order.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// The gateway's own reference for the payment — BOG's <c>transaction_id</c>, Stripe's
    /// payment intent. It is the number to quote when asking a bank what happened to a
    /// customer's money, and until now the only place it existed was a database column.
    /// </summary>
    public string? ProviderPaymentIntentId { get; set; }

    /*
      Offsets, not bare DateTimes, and that is a bug fix rather than a preference.

      Dapper hands back a DateTime with Kind=Unspecified for a datetime2 column, System.Text.Json
      serializes that WITHOUT a zone, and a browser reads a zoneless timestamp as local — so every
      time in this console was rendered four hours early in Tbilisi. The repository stamps
      DateTimeKind.Utc on the way out (as BekiAlarmRepository already does) and these carry the
      offset with them, which is the only version of the value a client cannot misread.
    */
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }

    /// <summary>Where the book itself got to — Completed, Failed, still generating.</summary>
    public string? BookStatus { get; set; }

    /// <summary>When the parent last opened the digital book. Null means they never have.</summary>
    public DateTimeOffset? LastReadAt { get; set; }

    /// <summary>
    /// The two files, said separately.
    ///
    /// One combined <c>HasPdf</c> stood here and it lied in the direction that costs most: a book
    /// whose press interior exists while the reading copy is withheld reported "PDF ready" to an
    /// operator whose customer could not download anything. The row now says which file exists,
    /// because "which" is the entire question.
    /// </summary>
    public bool HasReadingPdf { get; set; }
    public bool HasPrintPdf { get; set; }

    /// <summary>
    /// Unreviewed alarms against this order's book — things the pipeline waived and shipped past
    /// rather than died on. Read from the alarms table, which is one correlated count against an
    /// indexed column and the only release state cheap enough to carry on every row of a list.
    /// </summary>
    public int OpenAlarmCount { get; set; }

    /// <summary>
    /// A finished book whose reading copy was never published: the gates or a pending human review
    /// are holding it. WHY it is held lives on the detail response, which can afford to read the
    /// stored verdict; that it is held at all is visible from SQL alone, and that is the half the
    /// list needs to show without anybody expanding a row.
    /// </summary>
    public bool Withheld { get; set; }

    /// <summary>
    /// The one derived field, and the one the "needs attention" filter selects on: an open alarm,
    /// a failed book, money taken with nothing delivered, or a finished book being withheld.
    ///
    /// Derived in SQL rather than in the browser so the filter and the chip cannot disagree — a
    /// list that highlights different rows than it filters to is worse than neither.
    /// </summary>
    public bool NeedsAttention { get; set; }

    /// <summary>Where the parcel is, for a print order. Null for digital-only.</summary>
    public string? PrintStatus { get; set; }

    /// <summary>The parcel itself, so the row can link to the print queue without a second query.</summary>
    public Guid? PrintOrderId { get; set; }

    /// <summary>
    /// Who and where the book is about.
    ///
    /// On the row because the list is now searched by both, and a search that matches a column the
    /// row does not show is a result an operator cannot explain to themselves.
    /// </summary>
    public string? HeroName { get; set; }
    public string? WorldId { get; set; }

    /// <summary><c>beki</c> or <c>legacy</c> — which pipeline drew, or is drawing, this book.</summary>
    public string? GenerationPipeline { get; set; }

    /// <summary>
    /// Where the job has got to, for a row that is still being made. Both halves, because a
    /// percentage with no sentence and a sentence with no percentage each leave an operator
    /// guessing whether anything is happening.
    /// </summary>
    public int? ProgressPercent { get; set; }
    public string? ProgressMessage { get; set; }

    /// <summary>The last thing the generation job said about this book. Null if it never has.</summary>
    public DateTimeOffset? HeartbeatUtc { get; set; }

    /// <summary>
    /// Generating, and silent for longer than the stale-generation sweep tolerates.
    ///
    /// The same silence limit the sweep judges by, computed in the same SQL as the flag that
    /// filters on it — a list that highlights different rows than it filters to is worse than
    /// neither. A stale row is one the sweep is about to fail, or has failed to reach.
    /// </summary>
    public bool IsStale { get; set; }
}

/// <summary>
/// Everything about one order, for the panel that opens under its row: who bought it, what
/// they got, and where the parcel is. Assembled from four tables in one round trip, because a
/// panel that opens is a panel someone is waiting on.
/// </summary>
public sealed class AdminOrderDetailResponse
{
    public AdminOrderRow Order { get; set; } = new();
    public AdminOrderCustomer Customer { get; set; } = new();

    /// <summary>Null when the order never produced a book — an unpaid or failed order.</summary>
    public AdminOrderBook? Book { get; set; }

    /// <summary>Null for a digital-only order.</summary>
    public AdminOrderShipment? Shipment { get; set; }

    /// <summary>
    /// A person is being waited on: the render is fine and nobody has looked at it yet.
    ///
    /// Read from the stored release-gates verdict, which lives in blob storage — which is exactly
    /// why it is here and not on the row. Twenty-five rows would be twenty-five blob reads to
    /// paint one list, so the list carries <see cref="AdminOrderRow.Withheld"/> from SQL and this
    /// response, opened for one order somebody just clicked, says which kind of withhold it is.
    /// </summary>
    public bool AwaitingReview { get; set; }

    /// <summary>
    /// How many gates this book fails, from the same stored verdict. Zero alongside
    /// <see cref="AwaitingReview"/> is the good case: nothing is broken, somebody simply has not
    /// signed the contact sheet.
    /// </summary>
    public int FailingGateCount { get; set; }

    /// <summary>
    /// Whether this order may be driven through fulfilment again.
    ///
    /// Answered by the server so the console never guesses. A retry button that is enabled by a
    /// rule the browser invented is a button that reports "queued" and is then silently declined
    /// by the job it queued — which is exactly what the operator cannot see.
    /// </summary>
    public bool CanRetry { get; set; }

    /// <summary>
    /// A finished or failed Beki book with no live job: part or all of it can be drawn again.
    ///
    /// Distinct from <see cref="CanRetry"/>, which re-drives the ORDER. A redraw spends money on
    /// images for a book that already exists, so it is offered only where it would actually work.
    /// </summary>
    public bool CanRegenerate { get; set; }

    /// <summary>
    /// Every alarm ever raised against this book, newest first, reviewed ones included — the
    /// history behind the chip on the row, for somebody who has opened the order and wants to
    /// know whether this has happened before.
    /// </summary>
    public IReadOnlyList<AdminAlarmRow> Alarms { get; set; } = [];
}

public sealed class AdminOrderCustomer
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? DisplayName { get; set; }
    public string? PreferredLanguage { get; set; }
    public bool IsAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>How many books and orders this parent has in total, this one included.</summary>
    public int BookCount { get; set; }
    public int OrderCount { get; set; }
}

public sealed class AdminOrderBook
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? HeroName { get; set; }
    public string? WorldId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public int StoryPageCount { get; set; }
    public string? StoryLanguage { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? ProgressMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastReadAt { get; set; }

    /// <summary>
    /// Whether a PDF exists to download. The URLs themselves are not returned: they are blob
    /// paths this API resolves, and an admin downloads through
    /// <c>GET /api/admin/orders/{id}/pdf</c> rather than by being handed storage internals.
    /// </summary>
    public bool HasReadingPdf { get; set; }
    public bool HasPrintPdf { get; set; }

    /// <summary>Which pipeline drew this book, and how far its job has got.</summary>
    public string? GenerationPipeline { get; set; }
    public int? ProgressPercent { get; set; }
    public DateTimeOffset? HeartbeatUtc { get; set; }

    /// <summary>Generating, and silent for longer than the sweep tolerates.</summary>
    public bool IsStale { get; set; }

    /// <summary>The hero, so the console can link the book to the child it is about.</summary>
    public Guid? PrimaryCharacterId { get; set; }

    /// <summary>
    /// Which spreads exist in storage, by number.
    ///
    /// Probed rather than inferred from the status: a book that stopped on spread five has four
    /// pictures an operator can look at, and "Failed" says nothing about which. Eight cheap
    /// existence checks, on one order somebody deliberately opened — never per row of a list.
    /// </summary>
    public IReadOnlyList<int> SpreadsAvailable { get; set; } = [];

    /// <summary>Whether there is a cover image to show — the cropped front board, or the pack's own.</summary>
    public bool HasCoverImage { get; set; }

    /// <summary>Whether any contact sheet was rendered, which is what a reviewer signs.</summary>
    public bool HasContactSheet { get; set; }

    /// <summary>
    /// The machine code at the front of the stored error message, when it looks like one.
    ///
    /// The pipeline's failures are written as <c>CODE the rest of the sentence</c>, and the code is
    /// the half that groups incidents and matches a runbook. The whole message stays on
    /// <see cref="ErrorMessage"/>; this is only the handle.
    /// </summary>
    public string? FailureCode { get; set; }
}

public sealed class AdminOrderShipment
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The Georgian word for <see cref="Status"/>, from the same table the parcel emails use, so
    /// the console and the letter the parent received say the same thing.
    /// </summary>
    public string? StatusLabel { get; set; }

    /// <summary>
    /// The parcel's id, said again under its own name. It is <see cref="Id"/>, and the console
    /// deep-links to the print queue with it — a field called <c>id</c> inside a shipment object
    /// is exactly the one somebody eventually passes the order id to.
    /// </summary>
    public Guid PrintOrderId { get; set; }

    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? PostalCode { get; set; }
    public string? Notes { get; set; }
    public string? TrackingCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public sealed class AdminCustomerListResponse
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<AdminCustomerRow> Items { get; set; } = [];
}

public sealed class AdminCustomerRow
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? DisplayName { get; set; }
    public int BookCount { get; set; }
    public int OrderCount { get; set; }
    public long SpendMinor { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Body of <c>PUT /api/admin/users/{id}/admin</c>.</summary>
public sealed class UpdateUserAdminRequest
{
    public bool IsAdmin { get; set; }
}

/// <summary>
/// What the sixteen hard gates make of one book, flattened for the admin console.
///
/// A projection rather than the stored document itself: <c>release-gates.json</c> carries the
/// evidence blob names each gate rests on, and those are storage keys — useful in a handback zip
/// beside the files they point at, and nothing an operator's browser should be handed.
/// </summary>
/// <param name="Verdict">
/// <c>RELEASABLE</c>, <c>NOT_RELEASABLE</c>, or null for a book with no evaluation stored — which is
/// every book fulfilled before the release gates existed, and is shown rather than hidden.
/// </param>
/// <param name="ContactSheetSha256">
/// The rendering the human approval is about (amendment A2). The console sends it back with the
/// approval so that a reviewer signing a stale sheet is refused rather than recorded.
/// </param>
public sealed record AdminReleaseGatesResponse(
    string? Verdict,
    DateTimeOffset? EvaluatedAtUtc,
    IReadOnlyList<string> FailingGates,
    bool AwaitingHumanReview,
    string? ContactSheetSha256,
    bool CustomerPdfPublished,
    bool PressFilesPublished,
    IReadOnlyList<AdminReleaseGate> Gates);

/// <summary>One gate's verdict, as the console shows it.</summary>
public sealed record AdminReleaseGate(string Id, string Status, string Class, string Detail);

/// <summary>Body of <c>POST /api/admin/orders/{id}/approve-review</c>.</summary>
public sealed class AdminApproveReviewRequest
{
    /// <summary>What the reviewer wants on the record. Optional; trimmed; may be omitted.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// The contact sheet being approved, by SHA-256, exactly as the gate status reported it.
    /// A mismatch is a 409: the reviewer looked at a different rendering than the one on file.
    /// </summary>
    public string? ContactSheetSha256 { get; set; }
}

/// <summary>
/// Body of <c>POST /api/admin/orders/{id}/status</c> — the two marks an operator may put on an
/// order by hand.
///
/// Deliberately not a general status setter. Everything else about an order's state is written by
/// the machinery that knows: the gateway marks it Paid, fulfilment marks it Fulfilled. What a
/// person genuinely decides is that the money went back, or that this is never going to ship, and
/// those are the only two words this accepts.
/// </summary>
public sealed class AdminSetOrderStatusRequest
{
    /// <summary><c>Refunded</c> or <c>Cancelled</c>. Anything else is a 400.</summary>
    [Required, MaxLength(24)]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Why, for the record. Stored on the order's failure reason behind an <c>admin:</c> prefix,
    /// so a line a person typed is never mistaken for one the pipeline wrote.
    /// </summary>
    [MaxLength(500)]
    public string? Note { get; set; }
}

/// <summary>
/// Body of <c>POST /api/admin/books/{id}/regenerate</c>.
///
/// Every field is load-bearing. The scope decides how many paid image calls this costs — one
/// spread, the cover, or the whole book — and the reason is required because a redraw with no
/// stated cause is a bill nobody can account for afterwards.
/// </summary>
public sealed class AdminRegenerateBookRequest
{
    /// <summary><c>book</c>, <c>spread</c> or <c>cover</c>.</summary>
    [Required, MaxLength(16)]
    public string Scope { get; set; } = string.Empty;

    /// <summary>Which spread, 1–8. Required for the <c>spread</c> scope and ignored by the others.</summary>
    public int? Spread { get; set; }

    /// <summary>What is wrong with the current pictures. Kept on an alarm as the audit trail.</summary>
    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
