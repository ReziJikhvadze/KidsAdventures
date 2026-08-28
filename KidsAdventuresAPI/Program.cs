using AdventurePacks.Api.Data;
using AdventurePacks.Api.Extensions;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Hangfire;

DapperTypeHandlers.Register();

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAdventurePacksOptions(builder.Configuration)
    .AddAdventurePacksCors(builder.Configuration)
    .AddAdventurePacksData()
    .AddAdventurePacksAuth(builder.Configuration)
    .AddAdventurePacksInfrastructure(builder.Configuration)
    .AddAdventurePacksApplication()
    .AddFrontendHosting(builder.Configuration);

var app = builder.Build();

LogConfiguredFlags(app);

using (var scope = app.Services.CreateScope())
{
    var migrator = scope.ServiceProvider.GetRequiredService<ISqlDatabaseMigrator>();
    await migrator.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<IDatabaseSeeder>();
    await seeder.SeedAsync();
}

app.UseGlobalExceptionHandling();

if (app.Configuration.GetValue<bool>("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthorizationFilter()]
});

RecurringJob.AddOrUpdate<IAuthChallengeCleanupService>(
    "auth-challenges-purge",
    service => service.PurgeExpiredAsync(),
    Cron.Hourly);

// A parent whose payment landed while the app was restarting must still get their book.
// This sweep is the only thing standing between a crash mid-fulfilment and a charge with
// nothing to show for it, so it runs often and independently of the webhook.
RecurringJob.AddOrUpdate<IOrderService>(
    "orders-retry-fulfilment",
    service => service.RetryStalledFulfilmentAsync(),
    "*/5 * * * *");

// A guest preview stores a child's name, age and photograph so the browser has something to
// poll while the book is written. The expiry on those rows is the promise that none of it is
// kept; this is what makes the promise true. Hourly, because a day-long expiry does not need
// finer granularity and the sweep is cheap when there is nothing to do.
RecurringJob.AddOrUpdate<IMasterStoryRunCleanupService>(
    "guest-runs-purge",
    service => service.PurgeExpiredAsync(),
    Cron.Hourly);

app.MapControllers();
app.UseFrontendHosting();

app.Run();

/// <summary>
/// Prints what the switches actually say, before anything tries to read them as booleans.
///
/// This exists because Beki__PortraitGateEnabled was set to a value that would not parse, and
/// there was no way to see what it was: the app came up, the deploy went green, and the only
/// symptom was a 500 much later quoting a key. Half an hour went into guessing at a string.
///
/// Values are quoted and their length printed, so a trailing space, an empty box where the
/// Azure portal's second Save never landed, or a pasted invisible character is visible in the
/// log rather than looking exactly like the word it was meant to be. It runs before the
/// options validate at host start, so a value that is about to stop the deployment is on the
/// line above the failure.
///
/// Switches only. Never a key, a secret or a connection string — this goes to a log stream
/// that anybody with read access to the app can watch.
/// </summary>
static void LogConfiguredFlags(WebApplication application)
{
    string[] flags =
    [
        "Beki:Enabled",
        "Beki:PortraitGateEnabled",
        "Stripe:Enabled",
        "Stripe:BypassPayment",
        "Bog:Enabled",
        "Bog:VerifyCallbackSignature"
    ];

    foreach (var flag in flags)
    {
        var raw = application.Configuration[flag];
        application.Logger.LogInformation(
            "Config flag {Flag} = {Value} ({Length} chars)",
            flag,
            raw is null ? "<not set>" : $"\"{raw}\"",
            raw?.Length ?? 0);
    }
}
