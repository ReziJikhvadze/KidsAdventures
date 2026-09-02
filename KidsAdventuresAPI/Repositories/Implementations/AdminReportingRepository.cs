using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Repositories.Implementations;

/// <summary>
/// Read-only cross-customer queries for the operations screens.
///
/// Kept apart from the per-user repositories on purpose: everything here intentionally
/// omits a UserId predicate, and that is a property worth being able to see in one file
/// rather than hunting for across a dozen methods.
/// </summary>
public sealed class AdminReportingRepository(
    ISqlConnectionFactory connectionFactory,
    IOptions<BekiOptions> bekiOptions) : IAdminReportingRepository
{
    /// <summary>
    /// The one saved view worth a name: money taken with nothing delivered.
    ///
    /// It used to be a count on an overview page, where it could be read and not acted on.
    /// As a filter it lands on the list that can actually open the order.
    ///
    /// "Nothing delivered" is two different failures, and the obvious half is the smaller one.
    /// An order can be stuck before fulfilment ever ran — Paid with no FulfilledAt — but it can
    /// also be marked Fulfilled and still have delivered nothing: fulfilment's job is to create
    /// the book and queue it, so an order whose generation then failed is Fulfilled with a Failed
    /// book behind it. That parent has paid and has no book either way, so both belong here.
    /// </summary>
    public const string PaidUnfulfilledFlag = "paid-unfulfilled";

    /// <summary>
    /// The wider saved view: everything with something wrong with it.
    ///
    /// Money taken with nothing delivered is one shape of trouble and it was the only one this
    /// list could name. An unreviewed alarm, a failed book and a finished book whose file is being
    /// withheld are three more, and every one of them is a parent waiting on somebody here to
    /// notice. One filter selects them all, and it selects on the same expression the row's own
    /// chip is painted from — a list that highlights different rows than it filters to is worse
    /// than neither.
    /// </summary>
    public const string NeedsAttentionFlag = "needs-attention";

    /// <summary>A book with a job in front of it: claimed, planned, drawing, or laying out.</summary>
    public const string GeneratingFlag = "generating";

    /// <summary>
    /// Generating, and silent for longer than the stale-generation sweep tolerates.
    ///
    /// The one view an operator opens the console at nine in the morning to see. Everything in it
    /// is a paid book whose job has stopped saying anything, which the sweep is about to bury or
    /// has already failed to reach.
    /// </summary>
    public const string StuckFlag = "stuck";

    /// <summary>Finished, correct, and waiting on a person to sign the contact sheet.</summary>
    public const string AwaitingReviewFlag = "awaiting-review";

    /// <summary>The book stopped. Distinct from the order, which may still say Fulfilled.</summary>
    public const string FailedFlag = "failed";

    /// <summary>
    /// The six the list accepts. Named here rather than in the controller because the predicates
    /// they select are here — a filter the API advertises and the SQL does not implement is a
    /// short list shown to somebody who asked for the interesting one.
    /// </summary>
    public static readonly IReadOnlySet<string> Flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PaidUnfulfilledFlag, NeedsAttentionFlag, GeneratingFlag, StuckFlag, AwaitingReviewFlag, FailedFlag,
    };

    /// <summary>
    /// The four ways an order earns the operator's attention, as one SQL predicate so the filter
    /// and the row flag cannot drift apart.
    ///
    /// A cancelled order is excluded outright: whatever went wrong with a book nobody is paying
    /// for any more, it is not work.
    ///
    /// The last clause is the new one and the quiet one. A Completed book with no reading PDF is a
    /// finished book the release policy is holding back — it is the state that produced a
    /// download button throwing English at a parent, and until now nothing in this console showed
    /// it at all. Which KIND of withhold it is (a pending human review, or failing gates) lives in
    /// the stored verdict in blob storage and is answered by the detail response; that it is held
    /// is knowable from two columns, and that is the half a list of twenty-five rows can afford.
    /// </summary>
    private const string NeedsAttentionPredicate = """
        (o.Status <> N'Cancelled'
         AND (al.OpenAlarmCount > 0
              OR b.Status = N'Failed'
              OR (o.Status = N'Paid' AND o.FulfilledAt IS NULL)
              OR (b.Status = N'Completed' AND b.PdfUrl IS NULL)))
        """;

    /// <summary>
    /// A book with a job in front of it.
    ///
    /// <c>Pending</c> counts because a pack is created Pending and claimed a moment later, so the
    /// gap between the two is a book that is being made rather than one that is not. <c>Generating</c>
    /// is the legacy single-phase status. <c>StoryReady</c> counts only on the composite pipeline,
    /// and that asymmetry is amendment B5's whole point: a legacy book at StoryReady is finished
    /// text waiting to be illustrated on demand, while a Beki book at StoryReady is a stage inside
    /// a job that is still running and about to draw eight spreads.
    /// </summary>
    private const string GeneratingPredicate = """
        (b.Status IN (N'Pending', N'Generating', N'GeneratingStory', N'GeneratingPdf')
         OR (b.Status = N'StoryReady' AND b.GenerationPipeline = N'beki'))
        """;

    /// <summary>
    /// Silent for longer than the sweep's limit — the same COALESCE the sweep's own query uses,
    /// because a row that predates the heartbeat column must be judged by CreatedAt or it can
    /// never be reached at all.
    /// </summary>
    private const string SilentPredicate = "COALESCE(b.GenerationHeartbeatUtc, b.CreatedAt) < @StaleCutoffUtc";

    private const string AwaitingReviewPredicate =
        "(b.Status = N'Completed' AND b.PdfUrl IS NULL AND b.GenerationPipeline = N'beki')";

    /// <summary>
    /// Unreviewed alarms for this order's book.
    ///
    /// CROSS APPLY rather than a LEFT JOIN with a GROUP BY: the aggregate always yields exactly one
    /// row, so no order is dropped and no row is duplicated by a book with four alarms against it.
    /// The predicate is (PackId, ReviewedAtUtc IS NULL), which is the shape migration 035 indexes
    /// for exactly this read.
    /// </summary>
    private const string AlarmApply = """
        CROSS APPLY (
            SELECT COUNT(*) AS OpenAlarmCount
            FROM dbo.BekiAlarms a
            WHERE a.PackId = o.BookId AND a.ReviewedAtUtc IS NULL
        ) al
        """;

    /// <summary>
    /// The book columns every order view wants. Written once because the list and the detail must
    /// agree about them — the row shape is shared, and a column computed two ways is a column that
    /// eventually says two things.
    /// </summary>
    private static readonly string BookStateColumns = $"""
        CAST(CASE WHEN b.PdfUrl IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasReadingPdf,
        CAST(CASE WHEN b.PrintPdfUrl IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasPrintPdf,
        al.OpenAlarmCount,
        CAST(CASE WHEN b.Status = N'Completed' AND b.PdfUrl IS NULL
                  THEN 1 ELSE 0 END AS BIT) AS Withheld,
        CAST(CASE WHEN {NeedsAttentionPredicate} THEN 1 ELSE 0 END AS BIT) AS NeedsAttention,
        CAST(CASE WHEN {GeneratingPredicate} AND {SilentPredicate}
                  THEN 1 ELSE 0 END AS BIT) AS IsStale
        """;

    /// <summary>Every column the row projection selects, so the list and the detail cannot drift.</summary>
    private static readonly string OrderRowColumns = $"""
        o.Id, o.UserId, u.Email AS CustomerEmail, u.PhoneNumber AS CustomerPhone,
        o.BookId, b.Title AS BookTitle, o.Type, o.Package, o.Status, o.Currency,
        o.SubtotalMinor, o.DiscountMinor, o.TotalMinor, o.FailureReason,
        o.Provider, o.ProviderPaymentIntentId,
        o.CreatedAt, o.PaidAt, o.FulfilledAt,
        b.Status AS BookStatus, b.LastReadAt,
        {BookStateColumns},
        pr.Status AS PrintStatus, pr.Id AS PrintOrderId,
        c.Name AS HeroName, b.WorldId, b.GenerationPipeline,
        b.ProgressPercent, b.ProgressMessage, b.GenerationHeartbeatUtc AS HeartbeatUtc
        """;

    /// <summary>
    /// The joins the row projection needs. The Characters join is what makes the list searchable
    /// by the child's name, which is how support tickets actually arrive — a parent writes about
    /// "ნიკუშას წიგნი", not about an order id.
    /// </summary>
    private static readonly string OrderRowFrom = $"""
        FROM dbo.Orders o
        LEFT JOIN dbo.Users u ON u.Id = o.UserId
        LEFT JOIN dbo.AdventurePacks b ON b.Id = o.BookId
        LEFT JOIN dbo.Characters c ON c.Id = b.PrimaryCharacterId
        LEFT JOIN dbo.PrintOrders pr ON pr.OrderId = o.Id
        {AlarmApply}
        """;

    public async Task<AdminOrderListResponse> GetOrdersAsync(
        string? status,
        string? search,
        string? flag,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // The book and parcel columns are the answer to "did they actually get it", which is
        // what the list is opened to find out. LEFT JOIN throughout: an unpaid order has no
        // book, and a digital order has no parcel, and neither is a reason to drop the row.
        var from = OrderRowFrom;

        // One AND per saved view, each inert unless its parameter is set. The alternative — a
        // switch that builds a different WHERE per flag — is five predicates to keep in step with
        // the five columns the row is painted from, and they drift.
        var where = $"""
            WHERE (@Status IS NULL OR o.Status = @Status)
              AND (@PaidUnfulfilled = 0
                   OR (o.Status = N'Paid' AND o.FulfilledAt IS NULL)
                   OR (o.Status IN (N'Paid', N'Fulfilled') AND b.Status = N'Failed'))
              AND (@NeedsAttention = 0 OR {NeedsAttentionPredicate})
              AND (@Generating = 0 OR {GeneratingPredicate})
              AND (@Stuck = 0 OR ({GeneratingPredicate} AND {SilentPredicate}))
              AND (@AwaitingReview = 0 OR {AwaitingReviewPredicate})
              AND (@Failed = 0 OR b.Status = N'Failed')
              AND (@Search IS NULL
                   OR u.Email LIKE @Like
                   OR u.PhoneNumber LIKE @Like
                   OR u.DisplayName LIKE @Like
                   OR b.Title LIKE @Like
                   OR c.Name LIKE @Like
                   OR pr.RecipientName LIKE @Like
                   OR pr.RecipientPhone LIKE @Like
                   OR CAST(o.Id AS NVARCHAR(64)) LIKE @Like)
            """;

        var sql = $"""
            SELECT COUNT(*)
            {from}
            {where};

            SELECT {OrderRowColumns}
            {from}
            {where}
            ORDER BY o.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        var parameters = new
        {
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            PaidUnfulfilled = Is(flag, PaidUnfulfilledFlag),
            NeedsAttention = Is(flag, NeedsAttentionFlag),
            Generating = Is(flag, GeneratingFlag),
            Stuck = Is(flag, StuckFlag),
            AwaitingReview = Is(flag, AwaitingReviewFlag),
            Failed = Is(flag, FailedFlag),
            StaleCutoffUtc = StaleCutoffUtc(),
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%",
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        };

        using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<OrderRowData>()).Select(Map).ToList();

        return new AdminOrderListResponse { Total = total, Page = page, PageSize = pageSize, Items = items };
    }

    public async Task<AdminOrderDetailResponse?> GetOrderDetailAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        // Four result sets in one round trip. The panel opens under a row someone just
        // clicked, so the cost that matters is the number of trips, not the number of rows.
        var sql = $"""
            SELECT {OrderRowColumns}
            {OrderRowFrom}
            WHERE o.Id = @OrderId;

            SELECT u.Id, u.Email, u.PhoneNumber, u.DisplayName, u.PreferredLanguage,
                   u.IsAdmin, u.CreatedAt,
                   (SELECT COUNT(*) FROM dbo.AdventurePacks p WHERE p.UserId = u.Id) AS BookCount,
                   (SELECT COUNT(*) FROM dbo.Orders x WHERE x.UserId = u.Id) AS OrderCount
            FROM dbo.Users u
            JOIN dbo.Orders o ON o.UserId = u.Id
            WHERE o.Id = @OrderId;

            SELECT b.Id, b.Title, c.Name AS HeroName, b.WorldId, b.Status, b.SequenceNumber,
                   b.StoryPageCount, b.StoryLanguage, b.CoverImageUrl, b.ProgressMessage,
                   b.ErrorMessage, b.CreatedAt, b.LastReadAt,
                   b.GenerationPipeline, b.ProgressPercent, b.PrimaryCharacterId,
                   b.GenerationHeartbeatUtc AS HeartbeatUtc,
                   CAST(CASE WHEN b.PdfUrl IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasReadingPdf,
                   CAST(CASE WHEN b.PrintPdfUrl IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasPrintPdf,
                   CAST(CASE WHEN {GeneratingPredicate} AND {SilentPredicate}
                             THEN 1 ELSE 0 END AS BIT) AS IsStale
            FROM dbo.AdventurePacks b
            JOIN dbo.Orders o ON o.BookId = b.Id
            LEFT JOIN dbo.Characters c ON c.Id = b.PrimaryCharacterId
            WHERE o.Id = @OrderId;

            SELECT pr.Id, pr.Status, pr.RecipientName, pr.RecipientPhone, pr.City, pr.Region,
                   pr.AddressLine1, pr.AddressLine2, pr.PostalCode, pr.Notes, pr.TrackingCode,
                   pr.CreatedAt, pr.ShippedAt, pr.DeliveredAt
            FROM dbo.PrintOrders pr
            WHERE pr.OrderId = @OrderId;
            """;

        using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(
                sql,
                new { OrderId = orderId, StaleCutoffUtc = StaleCutoffUtc() },
                cancellationToken: cancellationToken));

        var order = await grid.ReadFirstOrDefaultAsync<OrderRowData>();
        if (order is null)
        {
            return null;
        }

        var customer = await grid.ReadFirstOrDefaultAsync<CustomerRowData>();
        var book = await grid.ReadFirstOrDefaultAsync<BookRowData>();
        var shipment = await grid.ReadFirstOrDefaultAsync<ShipmentRowData>();

        return new AdminOrderDetailResponse
        {
            Order = Map(order),
            Customer = customer is null ? new AdminOrderCustomer() : Map(customer),
            Book = book is null ? null : Map(book),
            Shipment = shipment is null ? null : Map(shipment)
        };
    }

    public async Task<AdminCustomerListResponse> GetCustomersAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        const string where = """
            WHERE (@Search IS NULL
                   OR u.Email LIKE @Like
                   OR u.PhoneNumber LIKE @Like
                   OR u.DisplayName LIKE @Like)
            """;

        // Admins first: this list is now also how one is granted, and the people who already
        // have the role are the ones being checked on.
        var sql = $"""
            SELECT COUNT(*) FROM dbo.Users u {where};

            SELECT u.Id, u.Email, u.PhoneNumber, u.DisplayName, u.IsAdmin, u.CreatedAt,
                   (SELECT COUNT(*) FROM dbo.AdventurePacks p WHERE p.UserId = u.Id) AS BookCount,
                   (SELECT COUNT(*) FROM dbo.Orders o WHERE o.UserId = u.Id) AS OrderCount,
                   (SELECT ISNULL(SUM(CAST(o.TotalMinor AS BIGINT)), 0) FROM dbo.Orders o
                      WHERE o.UserId = u.Id AND o.Status IN (N'Paid', N'Fulfilled')) AS SpendMinor
            FROM dbo.Users u
            {where}
            ORDER BY u.IsAdmin DESC, u.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        var parameters = new
        {
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%",
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        };

        using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<AdminCustomerRow>()).ToList();

        return new AdminCustomerListResponse { Total = total, Page = page, PageSize = pageSize, Items = items };
    }

    // -- staleness ---------------------------------------------------------------------------

    /// <summary>
    /// The moment before which a generating book counts as silent.
    ///
    /// The sweep's own limit, read from the same options — the console must not invent a second
    /// definition of "stuck", because an operator staring at a row the console calls healthy while
    /// the sweep is preparing to fail it has been told something false about their own system.
    /// </summary>
    private DateTime StaleCutoffUtc() =>
        DateTime.UtcNow - GenerationBudget.SweepSilenceLimit(bekiOptions.Value);

    private static int Is(string? flag, string name) =>
        string.Equals(flag, name, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    // -- mapping -----------------------------------------------------------------------------

    /*
      Why every read here goes through a row class.

      Dapper materializes a datetime2 column as a DateTime with Kind=Unspecified, and
      System.Text.Json writes an unspecified DateTime with no zone on it — so a browser in Tbilisi
      read every timestamp in this console four hours early. The fix is the one BekiAlarmRepository
      already applies: stamp DateTimeKind.Utc where the value leaves SQL, and expose a
      DateTimeOffset, which is the only form of the value a client cannot misread. Dapper cannot
      write into a DateTimeOffset property from a datetime2 column, so the shapes are separate: a
      row class Dapper fills, and a DTO that is mapped from it.
    */

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is { } moment ? Utc(moment) : null;

    private static AdminOrderRow Map(OrderRowData row) => new()
    {
        Id = row.Id,
        UserId = row.UserId,
        CustomerEmail = row.CustomerEmail,
        CustomerPhone = row.CustomerPhone,
        BookId = row.BookId,
        BookTitle = row.BookTitle,
        Type = row.Type,
        Package = row.Package,
        Status = row.Status,
        Currency = row.Currency,
        SubtotalMinor = row.SubtotalMinor,
        DiscountMinor = row.DiscountMinor,
        TotalMinor = row.TotalMinor,
        FailureReason = row.FailureReason,
        Provider = row.Provider,
        ProviderPaymentIntentId = row.ProviderPaymentIntentId,
        CreatedAt = Utc(row.CreatedAt),
        PaidAt = Utc(row.PaidAt),
        FulfilledAt = Utc(row.FulfilledAt),
        BookStatus = row.BookStatus,
        LastReadAt = Utc(row.LastReadAt),
        HasReadingPdf = row.HasReadingPdf,
        HasPrintPdf = row.HasPrintPdf,
        OpenAlarmCount = row.OpenAlarmCount,
        Withheld = row.Withheld,
        NeedsAttention = row.NeedsAttention,
        PrintStatus = row.PrintStatus,
        PrintOrderId = row.PrintOrderId,
        HeroName = row.HeroName,
        WorldId = row.WorldId,
        GenerationPipeline = row.GenerationPipeline,
        ProgressPercent = row.ProgressPercent,
        ProgressMessage = row.ProgressMessage,
        HeartbeatUtc = Utc(row.HeartbeatUtc),
        IsStale = row.IsStale,
    };

    private static AdminOrderCustomer Map(CustomerRowData row) => new()
    {
        Id = row.Id,
        Email = row.Email,
        PhoneNumber = row.PhoneNumber,
        DisplayName = row.DisplayName,
        PreferredLanguage = row.PreferredLanguage,
        IsAdmin = row.IsAdmin,
        CreatedAt = Utc(row.CreatedAt),
        BookCount = row.BookCount,
        OrderCount = row.OrderCount,
    };

    private static AdminOrderBook Map(BookRowData row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        HeroName = row.HeroName,
        WorldId = row.WorldId,
        Status = row.Status,
        SequenceNumber = row.SequenceNumber,
        StoryPageCount = row.StoryPageCount,
        StoryLanguage = row.StoryLanguage,
        CoverImageUrl = row.CoverImageUrl,
        ProgressMessage = row.ProgressMessage,
        ErrorMessage = row.ErrorMessage,
        CreatedAt = Utc(row.CreatedAt),
        LastReadAt = Utc(row.LastReadAt),
        HasReadingPdf = row.HasReadingPdf,
        HasPrintPdf = row.HasPrintPdf,
        GenerationPipeline = row.GenerationPipeline,
        ProgressPercent = row.ProgressPercent,
        HeartbeatUtc = Utc(row.HeartbeatUtc),
        IsStale = row.IsStale,
        PrimaryCharacterId = row.PrimaryCharacterId,
        FailureCode = AdminFailureCode.From(row.ErrorMessage),
    };

    private static AdminOrderShipment Map(ShipmentRowData row) => new()
    {
        Id = row.Id,
        PrintOrderId = row.Id,
        Status = row.Status,
        StatusLabel = Enum.TryParse<PrintOrderStatus>(row.Status, out var parsed)
            ? PrintOrderStatusText.Label(parsed)
            : row.Status,
        RecipientName = row.RecipientName,
        RecipientPhone = row.RecipientPhone,
        City = row.City,
        Region = row.Region,
        AddressLine1 = row.AddressLine1,
        AddressLine2 = row.AddressLine2,
        PostalCode = row.PostalCode,
        Notes = row.Notes,
        TrackingCode = row.TrackingCode,
        CreatedAt = Utc(row.CreatedAt),
        ShippedAt = Utc(row.ShippedAt),
        DeliveredAt = Utc(row.DeliveredAt),
    };

    private sealed class OrderRowData
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
        public string Provider { get; set; } = string.Empty;
        public string? ProviderPaymentIntentId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? FulfilledAt { get; set; }
        public string? BookStatus { get; set; }
        public DateTime? LastReadAt { get; set; }
        public bool HasReadingPdf { get; set; }
        public bool HasPrintPdf { get; set; }
        public int OpenAlarmCount { get; set; }
        public bool Withheld { get; set; }
        public bool NeedsAttention { get; set; }
        public string? PrintStatus { get; set; }
        public Guid? PrintOrderId { get; set; }
        public string? HeroName { get; set; }
        public string? WorldId { get; set; }
        public string? GenerationPipeline { get; set; }
        public int? ProgressPercent { get; set; }
        public string? ProgressMessage { get; set; }
        public DateTime? HeartbeatUtc { get; set; }
        public bool IsStale { get; set; }
    }

    private sealed class CustomerRowData
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? DisplayName { get; set; }
        public string? PreferredLanguage { get; set; }
        public bool IsAdmin { get; set; }
        public DateTime CreatedAt { get; set; }
        public int BookCount { get; set; }
        public int OrderCount { get; set; }
    }

    private sealed class BookRowData
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
        public DateTime CreatedAt { get; set; }
        public DateTime? LastReadAt { get; set; }
        public string? GenerationPipeline { get; set; }
        public int? ProgressPercent { get; set; }
        public Guid? PrimaryCharacterId { get; set; }
        public DateTime? HeartbeatUtc { get; set; }
        public bool HasReadingPdf { get; set; }
        public bool HasPrintPdf { get; set; }
        public bool IsStale { get; set; }
    }

    private sealed class ShipmentRowData
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public string RecipientName { get; set; } = string.Empty;
        public string RecipientPhone { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string AddressLine1 { get; set; } = string.Empty;
        public string? AddressLine2 { get; set; }
        public string? PostalCode { get; set; }
        public string? Notes { get; set; }
        public string? TrackingCode { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ShippedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }
}

/// <summary>
/// The machine code at the front of a stored failure message.
///
/// Every terminal write in this system stores a sentence that begins with a code —
/// <c>GENERATION_STALLED the job went quiet…</c>, <c>IMAGE_GENERATION_FAILED (spread 3)…</c> — and
/// the code is the half that groups incidents and matches a runbook. Read here rather than in the
/// browser so that "what counts as a code" is one rule: SHOUTING_SNAKE_CASE at the start of the
/// message, and nothing else. A message that does not begin with one yields null rather than its
/// first word, because "The" is not a failure code.
/// </summary>
public static class AdminFailureCode
{
    public static string? From(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return null;
        }

        var head = errorMessage.TrimStart();
        var end = head.AsSpan().IndexOfAny(' ', '(', ':');
        var candidate = end < 0 ? head : head[..end];

        return candidate.Length is >= 3 and <= 64
               && candidate.All(character => char.IsAsciiLetterUpper(character) || character is '_' or '-'
                   || char.IsAsciiDigit(character))
               && candidate.Any(char.IsAsciiLetterUpper)
            ? candidate
            : null;
    }
}
