using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// <inheritdoc cref="IAdminNotifier" />
///
/// Every method catches everything. That is not laziness about error handling — it is the
/// contract: these three calls sit inside a payment, a failure handler and an order confirmation,
/// and each of those has already done the thing that matters by the time this runs. An alert that
/// can throw is an alert that can roll back a sale.
/// </summary>
public sealed class AdminNotifier(
    IEmailService emailService,
    IUserRepository userRepository,
    IAdventurePackRepository packRepository,
    IOptions<EmailOptions> emailOptions,
    ILogger<AdminNotifier> logger) : IAdminNotifier
{
    private readonly EmailOptions _email = emailOptions.Value;

    public Task OrderPaidAsync(Order order, CancellationToken cancellationToken) =>
        SafelyAsync("order paid", order.Id, async () =>
        {
            var user = await userRepository.GetByIdAsync(order.UserId, cancellationToken);
            var book = order.BookId is { } bookId
                ? await packRepository.GetByIdNoOwnershipAsync(bookId, cancellationToken)
                : null;

            await emailService.SendAdminAlertAsync(
                $"ახალი შეკვეთა · {Money(order.TotalMinor)} · {order.Package}",
                "ახალი გადახდილი შეკვეთა შემოვიდა.",
                [
                    ("მომხმარებელი", user?.DisplayName ?? user?.Email ?? "—"),
                    ("ელფოსტა", user?.Email ?? "—"),
                    ("ტელეფონი", user?.PhoneNumber ?? "—"),
                    ("პაკეტი", $"{order.Package} · {order.Type}"),
                    ("წიგნი", book?.Title ?? "—"),
                    ("თანხა", Money(order.TotalMinor)),
                    ("ფასდაკლება", order.DiscountMinor > 0 ? Money(order.DiscountMinor) : "—"),
                    ("დრო", Moment(order.PaidAt ?? DateTime.UtcNow)),
                ],
                AdminOrderUrl(order.Id),
                cancellationToken);
        });

    public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken) =>
        SafelyAsync("book failed", packId, async () =>
        {
            var book = await packRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
            var user = book is null
                ? null
                : await userRepository.GetByIdAsync(book.UserId, cancellationToken);

            await emailService.SendAdminAlertAsync(
                "წიგნის გენერაცია ჩაფლავდა",
                "წიგნის შექმნა შეწყდა. თუ შეკვეთა გადახდილია, მომხმარებელს ფული ჩამოეჭრა და წიგნი არ მიუღია.",
                [
                    ("წიგნი", book?.Title ?? packId.ToString()),
                    ("სამყარო", book?.WorldId ?? "—"),
                    ("სტატუსი", book?.Status.ToString() ?? "—"),
                    ("მომხმარებელი", user?.Email ?? user?.PhoneNumber ?? "—"),
                    ("მიზეზი", Trim(reason, 400)),
                    ("დრო", Moment(DateTime.UtcNow)),
                ],
                AdminSearchUrl(user?.Email),
                cancellationToken);
        });

    public Task PrintOrderPlacedAsync(
        PrintOrder printOrder,
        string? bookTitle,
        CancellationToken cancellationToken) =>
        SafelyAsync("print order placed", printOrder.Id, async () =>
        {
            var user = await userRepository.GetByIdAsync(printOrder.UserId, cancellationToken);

            await emailService.SendAdminAlertAsync(
                $"ბეჭდვის შეკვეთა · {printOrder.City}",
                "ახალი ბეჭდური შეკვეთა დასაბეჭდად.",
                [
                    ("წიგნი", bookTitle ?? "—"),
                    ("მიმღები", printOrder.RecipientName),
                    ("ტელეფონი", printOrder.RecipientPhone),
                    ("ქალაქი", printOrder.City),
                    ("მისამართი", printOrder.AddressLine1),
                    ("მომხმარებელი", user?.Email ?? user?.PhoneNumber ?? "—"),
                    ("დრო", Moment(printOrder.CreatedAt)),
                ],
                AdminOrderUrl(printOrder.OrderId),
                cancellationToken);
        });

    private async Task SafelyAsync(string what, Guid id, Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin alert \"{What}\" for {Id} was not sent.", what, id);
        }
    }

    /// <summary>Deep-links straight to the row, so the mail is one click from the order.</summary>
    private string? AdminOrderUrl(Guid orderId) =>
        string.IsNullOrWhiteSpace(_email.BaseUrl)
            ? null
            : $"{_email.BaseUrl.TrimEnd('/')}/admin/orders?q={orderId}";

    private string? AdminSearchUrl(string? term) =>
        string.IsNullOrWhiteSpace(_email.BaseUrl) || string.IsNullOrWhiteSpace(term)
            ? null
            : $"{_email.BaseUrl.TrimEnd('/')}/admin/orders?q={Uri.EscapeDataString(term)}";

    private static string Money(int minor) => $"{minor / 100m:0.##} ₾";

    private static string Moment(DateTime utc) => $"{utc:yyyy-MM-dd HH:mm} UTC";

    /// <summary>A model's exception message can be a page long; an alert has room for a sentence.</summary>
    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
