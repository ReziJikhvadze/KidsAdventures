using System.Threading.RateLimiting;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Data;
using AdventurePacks.Api.Domain;
using AdventurePacks.Api.Repositories.Implementations;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Beki;
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
        services.Configure<BekiOptions>(configuration.GetSection(BekiOptions.SectionName));
        // App Service refuses an app setting named "AzureBlobStorage__ConnectionString"
        // ("AppSetting with name ... is not allowed"), so on Azure the value has to come
        // from the Connection strings section, which .NET surfaces as
        // ConnectionStrings:AzureBlobStorage. Bind the section first, then fall back to
        // that. Local appsettings.json keeps working unchanged.
        services.Configure<AzureBlobOptions>(options =>
        {
            configuration.GetSection(AzureBlobOptions.SectionName).Bind(options);
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                options.ConnectionString =
                    configuration.GetConnectionString(AzureBlobOptions.SectionName) ?? string.Empty;
            }
        });
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.SectionName));
        services.Configure<RecaptchaOptions>(configuration.GetSection(RecaptchaOptions.SectionName));
        services.Configure<PasswordlessAuthOptions>(configuration.GetSection(PasswordlessAuthOptions.SectionName));
        return services;
    }

    private static readonly string[] LocalhostDevOrigins =
    [
        "http://localhost:8080",
        "https://localhost:8080",
        "http://localhost:5173",
        "http://localhost:3000",
        "https://localhost:5173",
        "https://localhost:3000"
    ];

    public static IServiceCollection AddAdventurePacksCors(this IServiceCollection services, IConfiguration configuration)
    {
        var corsOptions = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
        var origins = corsOptions.AllowedOrigins.Length > 0
            ? corsOptions.AllowedOrigins.ToList()
            : [];

        if (corsOptions.AllowLocalhostFallback)
        {
            origins.AddRange(LocalhostDevOrigins);
        }
        else if (origins.Count == 0)
        {
            throw new InvalidOperationException(
                "Cors:AllowedOrigins is empty. Add your frontend URL(s) to appsettings.Production.json, " +
                "or set Cors:AllowLocalhostFallback to true for local development.");
        }

        var distinctOrigins = origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                policy.WithOrigins(distinctOrigins)
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

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole(UserRoles.Admin));
        });
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

        return services;
    }

    public static IServiceCollection AddAdventurePacksApplication(this IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPasswordlessAuthService, PasswordlessAuthService>();
        services.AddScoped<IAuthSessionFactory, AuthSessionFactory>();
        services.AddScoped<IWelcomeGiftService, WelcomeGiftService>();
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddScoped<IRecaptchaVerifier, RecaptchaVerifier>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IUserContextService, UserContextService>();
        services.AddScoped<IAuthChallengeCleanupService, AuthChallengeCleanupService>();

        // Swap this for a Georgian gateway client when one is contracted; nothing in the
        // auth flow knows the difference.
        services.AddScoped<ISmsSender, LoggingSmsSender>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthChallengeRepository, AuthChallengeRepository>();
        services.AddScoped<IGuestPreviewRepository, GuestPreviewRepository>();
        services.AddScoped<IChildRepository, ChildRepository>();
        services.AddScoped<ICharacterRepository, CharacterRepository>();
        services.AddScoped<ICharacterService, CharacterService>();
        services.AddScoped<IWorldRepository, WorldRepository>();
        services.AddScoped<IWorldProgressService, WorldProgressService>();
        services.AddScoped<ISeriesMemoryRepository, SeriesMemoryRepository>();
        services.AddScoped<IStoryRuleRepository, StoryRuleRepository>();
        services.AddScoped<IAdminReportingRepository, AdminReportingRepository>();
        services.AddScoped<ISeriesMemoryService, SeriesMemoryService>();
        services.AddScoped<IBookCastResolver, BookCastResolver>();
        services.AddScoped<IFamilyMemberRepository, FamilyMemberRepository>();
        services.AddScoped<IAdventurePackRepository, AdventurePackRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IPromoCodeRepository, PromoCodeRepository>();
        services.AddScoped<IPrintOrderRepository, PrintOrderRepository>();
        services.AddScoped<IUserAddressRepository, UserAddressRepository>();

        services.AddScoped<IPromoCodeService, PromoCodeService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBookFulfillmentService, BookFulfillmentService>();
        services.AddScoped<IPrintOrderService, PrintOrderService>();

        services.AddScoped<IReferenceImageNormalizer, ReferenceImageNormalizer>();
        services.AddScoped<IOpenAiService, OpenAiService>();
        services.AddScoped<IAdventurePdfService, AdventurePdfService>();
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
        services.AddScoped<IAdventureGenerationService, AdventureGenerationService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddSingleton<IGuestRateLimiter, GuestRateLimiter>();

        // Beki pipelines. Registered unconditionally so the services can be resolved and
        // exercised in staging; BekiOptions.Enabled is the flag that decides whether any
        // caller actually routes a book through them, keeping the old flow available for
        // side-by-side comparison until the regression set passes.
        services.AddSingleton<IBekiPromptProvider, BekiPromptProvider>();
        services.AddSingleton<IBekiCreativeSeedPool, BekiCreativeSeedPool>();
        services.AddSingleton<BekiStoryValidator>();
        services.AddSingleton<BekiSceneSpecBuilder>();
        services.AddScoped<IBekiOpenAiClient, BekiOpenAiClient>();
        services.AddScoped<IBekiStoryPipeline, BekiStoryPipeline>();
        services.AddScoped<IBekiVisualPipeline, BekiVisualPipeline>();

        return services;
    }
}
