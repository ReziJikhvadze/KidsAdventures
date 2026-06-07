using AdventurePacks.Api.Data;
using AdventurePacks.Api.Extensions;
using AdventurePacks.Api.Infrastructure;
using Hangfire;

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

app.MapControllers();
app.UseFrontendHosting();

app.Run();
