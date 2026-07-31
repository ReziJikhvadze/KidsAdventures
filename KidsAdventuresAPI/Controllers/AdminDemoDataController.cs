using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// Fills a database with plausible operational data so the admin screens can be built and
/// reviewed against something that looks like a real week of trading.
///
/// Development only, and it says so twice: the route is gated by the Admin policy AND the
/// handler refuses to run outside the Development environment. Seeding fake paid orders
/// into production would corrupt real revenue reporting.
///
/// It writes through the ordinary repositories rather than raw SQL, so the rows it creates
/// obey the same constraints and defaults as rows created by a real parent.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin/demo-data")]
public sealed class AdminDemoDataController(
    IUserRepository userRepository,
    ICharacterRepository characterRepository,
    IAdventurePackRepository packRepository,
    IOrderRepository orderRepository,
    IWebHostEnvironment environment,
    ILogger<AdminDemoDataController> logger) : ControllerBase
{
    private const string DemoEmailSuffix = "@demo.adventrya.local";

    private static readonly string[] FirstNames =
        ["ზუკა", "ნიტა", "ელენე", "გიო", "მარიამ", "ლუკა", "ანა", "სანდრო", "ნინო", "დათო"];

    private static readonly string[] Worlds =
        ["dinosaurs", "space", "pirates", "animals", "airplanes", "magic"];

    private static readonly ThemeType[] Themes =
        [ThemeType.Dinosaurs, ThemeType.Space, ThemeType.Pirates,
         ThemeType.Animals, ThemeType.Airplanes, ThemeType.Magic];

    [HttpPost]
    public async Task<ActionResult<object>> Seed(
        [FromQuery] int parents = 12,
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
        {
            return BadRequest(new { message = "Demo data can only be seeded in Development." });
        }

        if (parents is < 1 or > 100)
        {
            return BadRequest(new { message = "parents must be between 1 and 100." });
        }

        var random = new Random(20260801);
        var createdUsers = 0;
        var createdBooks = 0;
        var createdOrders = 0;

        for (var i = 0; i < parents; i++)
        {
            var heroName = FirstNames[random.Next(FirstNames.Length)];
            var email = $"demo-{Guid.NewGuid():N}"[..16] + DemoEmailSuffix;

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                DisplayName = $"{heroName}ს მშობელი",
                PhoneNumber = $"+9955{random.Next(10000000, 99999999)}",
                PhoneConfirmed = true,
                EmailConfirmed = true,
                PreferredLanguage = "ka",
                SubscriptionType = SubscriptionType.Free,
                // Spread signups over the last 60 days so date filters have something to bite on.
                CreatedAt = DateTime.UtcNow.AddDays(-random.Next(0, 60)).AddHours(-random.Next(0, 24))
            };
            await userRepository.CreateAsync(user, cancellationToken);
            createdUsers++;

            var hero = new Character
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Name = heroName,
                BirthDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-random.Next(3, 11))),
                Gender = random.Next(2) == 0 ? "girl" : "boy",
                EyeColor = new[] { "brown", "blue", "green", "grey" }[random.Next(4)],
                CharacterType = CharacterTraits.TypeChild,
                IsPrimary = true,
                CreatedAt = user.CreatedAt
            };
            await characterRepository.CreateAsync(hero, cancellationToken);

            // One to three books per parent, numbered as a series so the continuity work
            // and the chapter columns have realistic values to display.
            var bookCount = random.Next(1, 4);
            for (var sequence = 1; sequence <= bookCount; sequence++)
            {
                var worldIndex = random.Next(Worlds.Length);
                var isPrint = random.Next(100) < 35;
                var createdAt = user.CreatedAt.AddDays(sequence * random.Next(2, 12));
                if (createdAt > DateTime.UtcNow) createdAt = DateTime.UtcNow.AddHours(-1);

                // Most books finish; a few are left mid-flight so the production queue is
                // not uniformly green.
                var status = random.Next(100) switch
                {
                    < 70 => AdventurePackStatus.StoryReady,
                    < 82 => AdventurePackStatus.Completed,
                    < 92 => AdventurePackStatus.GeneratingStory,
                    _ => AdventurePackStatus.Failed
                };

                var book = new AdventurePack
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Theme = Themes[worldIndex],
                    Status = status,
                    StoryLanguage = "ka",
                    StoryPageCount = AdventureStoryConstants.FullPageCount,
                    AccessLevel = BookAccessLevel.Full,
                    HasPrintEntitlement = isPrint,
                    WorldId = Worlds[worldIndex],
                    PrimaryCharacterId = hero.Id,
                    SeriesId = hero.Id,
                    SequenceNumber = sequence,
                    Title = $"{heroName} და {WorldTitle(Worlds[worldIndex])}",
                    ProgressMessage = status == AdventurePackStatus.Failed
                        ? "რაღაც შეფერხდა. სცადე ხელახლა ან აირჩიე სხვა თემა."
                        : "წიგნი მზადაა! გახსენი ბიბლიოთეკაში.",
                    ErrorMessage = status == AdventurePackStatus.Failed ? "Generation timed out." : null,
                    CreatedAt = createdAt
                };
                await packRepository.CreatePendingAsync(book, cancellationToken);
                createdBooks++;

                var subtotal = isPrint ? GelPricing.PrintMinor : GelPricing.DigitalMinor;
                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    BookId = book.Id,
                    Type = OrderType.NewBook,
                    Package = isPrint ? OrderPackage.Print : OrderPackage.Digital,
                    SubtotalMinor = subtotal,
                    DiscountMinor = 0,
                    TotalMinor = subtotal,
                    Status = status == AdventurePackStatus.Failed
                        ? OrderStatus.Paid
                        : OrderStatus.Fulfilled,
                    Provider = OrderProviders.Stripe,
                    CreatedAt = createdAt,
                    PaidAt = createdAt.AddMinutes(2),
                    FulfilledAt = status == AdventurePackStatus.Failed ? null : createdAt.AddMinutes(6)
                };
                await orderRepository.CreateAsync(order, cancellationToken);
                createdOrders++;
            }
        }

        logger.LogWarning(
            "Demo data seeded: {Users} parents, {Books} books, {Orders} orders.",
            createdUsers, createdBooks, createdOrders);

        return Ok(new
        {
            parents = createdUsers,
            books = createdBooks,
            orders = createdOrders,
            note = "Demo rows use the " + DemoEmailSuffix + " email suffix and can be deleted with DELETE on this route."
        });
    }

    /// <summary>Removes everything this controller created, matched on the demo email suffix.</summary>
    [HttpDelete]
    public async Task<ActionResult<object>> Purge(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return BadRequest(new { message = "Demo data can only be purged in Development." });
        }

        var removed = await userRepository.PurgeDemoAccountsAsync(DemoEmailSuffix, cancellationToken);
        logger.LogWarning("Demo data purged: {Users} demo parents removed.", removed);
        return Ok(new { removedParents = removed });
    }

    private static string WorldTitle(string worldId) => worldId switch
    {
        "dinosaurs" => "დაკარგული ხეობის საიდუმლო",
        "space" => "ვარსკვლავის გზა",
        "pirates" => "მბრწყინავი კუნძული",
        "animals" => "მოჯადოებული ტყე",
        "airplanes" => "ღრუბლების ქალაქი",
        _ => "სინათლის ქალაქი",
    };
}
