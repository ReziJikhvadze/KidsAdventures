using System.Threading.RateLimiting;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Data;
using AdventurePacks.Api.Repositories.Implementations;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Interfaces;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AdventurePacks.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAdventurePacksOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.Configure<AzureBlobOptions>(configuration.GetSection(AzureBlobOptions.SectionName));
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        return services;
    }

    public static IServiceCollection AddAdventurePacksCors(this IServiceCollection services, IConfiguration configuration)
    {
        var corsOptions = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
        var origins = corsOptions.AllowedOrigins.Length > 0
            ? corsOptions.AllowedOrigins
            : throw new InvalidOperationException("Cors:AllowedOrigins must be set in appsettings.Production.json (your Azure frontend URL).");

        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return services;
    }

    public static IServiceCollection AddAdventurePacksData(this IServiceCollection services)
    {
        services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<ISqlDatabaseMigrator, SqlDatabaseMigrator>();
        services.AddScoped<IDatabaseSeeder, DatabaseSeeder>();
        return services;
    }

    public static IServiceCollection AddAdventurePacksAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                        ?? throw new InvalidOperationException("Jwt settings are missing.");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddAdventurePacksInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        services.AddHangfire(config =>
            config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                {
                    PrepareSchemaIfNecessary = true
                }));
        services.AddHangfireServer();

        services.AddHttpClient("OpenAI", (sp, client) =>
        {
            var openAi = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            client.BaseAddress = new Uri(openAi.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(90);
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: "global-api",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        services.AddHttpContextAccessor();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddControllers();

        var stripe = configuration.GetSection(StripeOptions.SectionName).Get<StripeOptions>();
        if (!string.IsNullOrWhiteSpace(stripe?.SecretKey))
        {
            Stripe.StripeConfiguration.ApiKey = stripe.SecretKey;
        }

        return services;
    }

    public static IServiceCollection AddAdventurePacksApplication(this IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IUserContextService, UserContextService>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IChildRepository, ChildRepository>();
        services.AddScoped<IFamilyMemberRepository, FamilyMemberRepository>();
        services.AddScoped<IAdventurePackRepository, AdventurePackRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();

        services.AddScoped<IOpenAiService, OpenAiService>();
        services.AddScoped<IAdventurePdfService, AdventurePdfService>();
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IAdventureGenerationService, AdventureGenerationService>();

        return services;
    }
}
