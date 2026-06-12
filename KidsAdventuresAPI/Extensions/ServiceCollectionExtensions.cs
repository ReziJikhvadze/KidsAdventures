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
        services.Configure<DodoPaymentsOptions>(configuration.GetSection(DodoPaymentsOptions.SectionName));
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        return services;
    }

    private static readonly string[] LocalhostDevOrigins =
    [
        "http://localhost:5173",
        "http://localhost:3000",
        "https://localhost:5173",
        "https://localhost:3000"
    ];

    public static IServiceCollection AddAdventurePacksCors(this IServiceCollection services, IConfiguration configuration)
    {
        var corsOptions = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
        var origins = corsOptions.AllowedOrigins.Length > 0
            ? corsOptions.AllowedOrigins
            : corsOptions.AllowLocalhostFallback
                ? LocalhostDevOrigins
                : throw new InvalidOperationException(
                    "Cors:AllowedOrigins is empty. Add your frontend URL(s) to appsettings.Production.json, " +
                    "or set Cors:AllowLocalhostFallback to true in appsettings.json for local development.");

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
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
        if (jwtOptions is null || string.IsNullOrWhiteSpace(jwtOptions.SecretKey))
        {
            throw new InvalidOperationException(
                "Jwt settings are missing. Copy appsettings.Production.example.json to appsettings.Production.json " +
                "and set Jwt:SecretKey (at least 32 characters).");
        }

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
            client.Timeout = TimeSpan.FromMinutes(6);
        });

        var permitLimit = configuration.GetValue("RateLimiting:PermitLimitPerMinute", 500);
        var disableForLocalhost = configuration.GetValue("RateLimiting:DisableForLocalhost", true);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (disableForLocalhost && remoteIp is "127.0.0.1" or "::1")
                {
                    return RateLimitPartition.GetNoLimiter(remoteIp);
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: remoteIp,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
        });

        services.AddHttpContextAccessor();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options => options.UseInlineDefinitionsForEnums());
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        var stripe = configuration.GetSection(StripeOptions.SectionName).Get<StripeOptions>();
        if (stripe?.Enabled == true && !string.IsNullOrWhiteSpace(stripe.SecretKey))
        {
            Stripe.StripeConfiguration.ApiKey = stripe.SecretKey;
        }

        services.AddSingleton<DodoPayments.Client.DodoPaymentsClient>(sp =>
        {
            var dodo = sp.GetRequiredService<IOptions<DodoPaymentsOptions>>().Value;
            return new DodoPayments.Client.DodoPaymentsClient
            {
                BearerToken = dodo.ApiKey,
                WebhookKey = dodo.WebhookSecret,
                BaseUrl = dodo.UseTestMode
                    ? "https://test.dodopayments.com"
                    : "https://live.dodopayments.com",
            };
        });

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
        services.AddScoped<IBookCreditPurchaseRepository, BookCreditPurchaseRepository>();

        services.AddScoped<IReferenceImageNormalizer, ReferenceImageNormalizer>();
        services.AddScoped<IOpenAiService, OpenAiService>();
        services.AddScoped<IAdventurePdfService, AdventurePdfService>();
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IAdventureGenerationService, AdventureGenerationService>();
        services.AddScoped<IEmailService, SmtpEmailService>();

        return services;
    }
}
