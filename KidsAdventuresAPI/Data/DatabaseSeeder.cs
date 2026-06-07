using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Data;

public interface IDatabaseSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

public sealed class DatabaseSeeder(
    IOptions<SeedOptions> seedOptions,
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ILogger<DatabaseSeeder> logger) : IDatabaseSeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var options = seedOptions.Value;
        if (!options.Enabled)
        {
            return;
        }

        await SeedUserAsync(
            options.DemoEmail,
            options.DemoPassword,
            SubscriptionType.Free,
            cancellationToken);

        if (options.CreatePremiumDemoUser)
        {
            await SeedUserAsync(
                options.PremiumDemoEmail,
                options.DemoPassword,
                SubscriptionType.Premium,
                cancellationToken);
        }

        logger.LogInformation("Database seed completed.");
    }

    private async Task SeedUserAsync(
        string email,
        string password,
        SubscriptionType subscriptionType,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Seed skipped — user {Email} already exists.", normalizedEmail);
            return;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = passwordHasher.Hash(password),
            SubscriptionType = subscriptionType,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        await userRepository.CreateAsync(user, cancellationToken);
        logger.LogInformation("Seeded user {Email} ({Plan}).", normalizedEmail, subscriptionType);
    }
}
