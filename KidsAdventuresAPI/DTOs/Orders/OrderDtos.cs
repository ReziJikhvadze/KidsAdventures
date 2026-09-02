using AdventurePacks.Api.DTOs.Print;

namespace AdventurePacks.Api.DTOs.Orders;

/// <summary>The create-journey draft the client sends at checkout for a brand-new book.</summary>
public sealed class BookDraftRequest
{
    /// <summary>The hero. Must be one of the caller's characters, flagged primary.</summary>
    [Required]
    public Guid PrimaryCharacterId { get; set; }

    /// <summary>Supporting cast, at most two on top of the hero.</summary>
    public List<Guid> SupportingCharacterIds { get; set; } = [];

    /// <summary>World slug, e.g. "dinosaurs".</summary>
    [Required, MaxLength(32)]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>Language the book is written in: "ka" or "en".</summary>
    [MaxLength(8)]
    public string BookLanguage { get; set; } = "ka";

    [MaxLength(1000)]
    public string? StoryNotes { get; set; }

    /// <summary>Set when this book continues an earlier one, carrying its threads forward.</summary>
    public Guid? ContinuesFromBookId { get; set; }

    /// <summary>The teaser the parent already saw, so the paid book keeps that opening.</summary>
    public Guid? PreviewBookId { get; set; }

    /// <summary>
    /// The story the parent was actually shown in the preview, serialized exactly as the
    /// model returned it.
    ///
    /// Without this the paid book is written from scratch, so a parent reads one story,
    /// pays, and receives a different one — the product breaking its own promise at the
    /// moment money changes hands. When present the book keeps this text verbatim and no
    /// second story call is made.
    /// </summary>
    public string? PreviewStoryJson { get; set; }

    /// <summary>
    /// The cover image from that preview, as a data URL. Reused as page one rather than
    /// redrawn, so the cover is the one the parent chose and only the remaining pages
    /// cost a generation.
    /// </summary>
    public string? PreviewCoverImage { get; set; }
}

public sealed class CreateOrderRequest
{
    /// <summary>"Digital" or "Print".</summary>
    [Required, MaxLength(16)]
    public string Package { get; set; } = "Digital";

    [MaxLength(64)]
    public string? PromoCode { get; set; }

    /// <summary>Required for a new book; omitted for a print upgrade.</summary>
    public BookDraftRequest? Draft { get; set; }

    /// <summary>Required for the Print package, ignored for Digital.</summary>
    public ShippingAddressRequest? ShippingAddress { get; set; }

    /// <summary>Where the provider returns the parent. Relative path, same-origin only.</summary>
    [MaxLength(256)]
    public string? ReturnPath { get; set; }
}

public sealed class CreatePrintUpgradeOrderRequest
{
    [Required]
    public Guid BookId { get; set; }

    [MaxLength(64)]
    public string? PromoCode { get; set; }

    /// <summary>Where to send the printed copy. Required.</summary>
    public ShippingAddressRequest? ShippingAddress { get; set; }

    [MaxLength(256)]
    public string? ReturnPath { get; set; }
}

/// <summary>What a promo code would do to a given package, without committing to anything.</summary>
public sealed class QuoteRequest
{
    /// <summary>"NewBook" or "PrintUpgrade".</summary>
    [MaxLength(24)]
    public string Type { get; set; } = "NewBook";

    [Required, MaxLength(16)]
    public string Package { get; set; } = "Digital";

    [MaxLength(64)]
    public string? PromoCode { get; set; }
}

public sealed class QuoteResponse
{
    public string Currency { get; set; } = GelPricing.Currency;
    public int SubtotalMinor { get; set; }
    public int DiscountMinor { get; set; }
    public int TotalMinor { get; set; }

    /// <summary>True when a full-discount code brought the total to zero.</summary>
    public bool IsFree { get; set; }

    public PromoQuote? Promo { get; set; }
}

public sealed class PromoQuote
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>False when the code exists but cannot be used; <see cref="Message"/> says why.</summary>
    public bool IsValid { get; set; }

    public int? PercentOff { get; set; }
    public bool IsFullDiscount { get; set; }
    public int DiscountMinor { get; set; }

    /// <summary>Georgian text ready to render under the promo field.</summary>
    public string? Message { get; set; }
}

public sealed class OrderResponse
{
    public Guid Id { get; set; }
    public Guid? BookId { get; set; }
    public OrderType Type { get; set; }
    public OrderPackage Package { get; set; }
    public string Currency { get; set; } = GelPricing.Currency;
    public int SubtotalMinor { get; set; }
    public int DiscountMinor { get; set; }
    public int TotalMinor { get; set; }
    public OrderStatus Status { get; set; }
    public string? PromoCode { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? FulfilledAt { get; set; }
}

/// <summary>
/// The answer to "I want to buy this". Either a provider URL to send the browser to, or
/// — when a full-discount code made the order free — an already-paid order and the book
/// it is generating.
/// </summary>
public sealed class CheckoutResponse
{
    public Guid OrderId { get; set; }
    public int TotalMinor { get; set; }
    public string Currency { get; set; } = GelPricing.Currency;

    /// <summary>True when no payment was needed and the book is already being generated.</summary>
    public bool IsFree { get; set; }

    /// <summary>Null for a free order.</summary>
    public string? CheckoutUrl { get; set; }

    public string? ProviderSessionId { get; set; }

    /// <summary>Set as soon as the book row exists, which for a free order is immediately.</summary>
    public Guid? BookId { get; set; }
}

/// <summary>What the client polls after returning from the provider.</summary>
public sealed class OrderStatusResponse
{
    public Guid OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public Guid? BookId { get; set; }

    /// <summary>True once the book is fully readable.</summary>
    public bool BookReady { get; set; }

    /// <summary>Georgian progress line, mirroring the pack's own progress message.</summary>
    public string? ProgressMessage { get; set; }

    /// <summary>
    /// How far the running job has got, 0–100, or null when nothing is running — the pack's own
    /// number, so a bar and its caption never disagree. Null before the book row exists.
    /// </summary>
    public int? ProgressPercent { get; set; }

    /// <summary>
    /// The pack's status as stored ("Pending", "StoryReady", "GeneratingStory", …), for a screen
    /// that wants to know <em>which</em> wait this is rather than only that it is waiting. Null
    /// before the book row exists.
    /// </summary>
    public string? PackStatus { get; set; }

    /// <summary>
    /// When the generation job last wrote anything to the book. Null for a pack that has never
    /// been claimed, and before the book row exists. The same column the stale-generation sweep
    /// judges by, so a screen can tell a slow book from a silent one.
    /// </summary>
    public DateTime? HeartbeatUtc { get; set; }

    /// <summary>The book's title once it has one; null until the pipeline writes it.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// The world slug the book is set in. From the pack once it exists, from the order's frozen
    /// draft before then — so a generating screen reloaded with nothing but an order id still
    /// knows which world it is showing.
    /// </summary>
    public string? WorldId { get; set; }

    /// <summary>
    /// The hero's name — the child the book is about. Resolved from the hero character rather
    /// than copied from anywhere, and null when it cannot be: a status poll never fails over a
    /// name.
    /// </summary>
    public string? ChildName { get; set; }

    /// <summary>The pack's cover image URL as stored; null until the cover is adopted or drawn.</summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// True when the book this order paid for could not be made.
    ///
    /// Distinct from <see cref="Status"/>, which is the <em>order's</em> status and stays
    /// Fulfilled: an order is marked fulfilled the moment generation is enqueued, so a book that
    /// dies an hour later leaves a perfectly healthy-looking order behind it. Without this flag
    /// the generating screen polls a Fulfilled order with <see cref="BookReady"/> false forever,
    /// which is exactly what a paid parent saw.
    /// </summary>
    public bool BookFailed { get; set; }

    /// <summary>
    /// What to tell the parent, in Georgian, when <see cref="BookFailed"/> is true. Null otherwise.
    ///
    /// Written by <see cref="Services.Story.ParentFacingFailure"/> from the book's stored failure,
    /// never the stored failure itself: that string is an English code and a stage name, and it is
    /// for the operator reading the admin page.
    /// </summary>
    public string? ParentMessage { get; set; }

    /// <summary>
    /// Why the <em>payment</em> failed — never why the book did. A parent whose card was declined
    /// and a parent whose book could not be drawn are in two different situations, and collapsing
    /// them into one field is how a generation failure came to be reported as a payment problem.
    /// </summary>
    public string? FailureReason { get; set; }
}
