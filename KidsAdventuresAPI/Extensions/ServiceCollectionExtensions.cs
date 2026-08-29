using System.Threading.RateLimiting;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Data;
using AdventurePacks.Api.Domain;
using AdventurePacks.Api.Repositories.Implementations;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Ai;
using AdventurePacks.Api.Services.Beki;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Interfaces;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AdventurePacks.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAdventurePacksOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        // ValidateOnStart, for the binding rather than for a rule.
        //
        // These sections come from App Service settings, where every value is a string somebody
        // typed into a box. A bool that says anything other than true or false — a stray "1", a
        // pasted character, an empty field where the second Save never landed — makes the binder
        // throw. Options bind on first resolution, so that throw does not land on the deployment
        // that caused it: it lands weeks later, as a 500, on the parent who happened to upload a
        // photo. The site is up, the deploy was green, and nothing says which setting is wrong.
        //
        // Binding at startup instead costs nothing and moves the failure to the one moment
        // somebody is watching. The message .NET gives names the exact key.
        services.AddOptions<BekiOptions>()
            .Bind(configuration.GetSection(BekiOptions.SectionName))
            .ValidateOnStart();
        // Validated at boot rather than trusted. A misspelled provider name would otherwise fall
        // through to the default and keep billing the vendor everyone believed had just been
        // switched away from; a Gemini setting with no key would take the failure all the way to
        // the middle of a paid book.
        //
        // ValidateOnStart is the whole point: without it these run on first resolution, which is
        // the first parent to ask for a story — a 500 for them instead of a deployment that
        // refuses to come up, which is the one moment somebody is watching.
        services.AddOptions<AiProviderOptions>()
            .Bind(configuration.GetSection(AiProviderOptions.SectionName))
            .Validate(
                options => AiProvider.IsKnown(options.Story),
                $"Providers:Story must be \"{AiProvider.OpenAi}\" or \"{AiProvider.Gemini}\".")
            .Validate(
                options => AiProvider.IsKnown(options.Images),
                $"Providers:Images must be \"{AiProvider.OpenAi}\" or \"{AiProvider.Gemini}\".")
            .Validate(
                options => AiProvider.IsKnownOrInherited(options.StoryPolish),
                $"Providers:StoryPolish must be \"{AiProvider.OpenAi}\", \"{AiProvider.Gemini}\", "
                + "or empty to follow Providers:Story.")
            .ValidateOnStart();

        services.AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .Validate(
                options =>
                {
                    var providers = new AiProviderOptions();
                    configuration.GetSection(AiProviderOptions.SectionName).Bind(providers);
                    return !providers.UsesGeminiAnywhere
                           || !string.IsNullOrWhiteSpace(options.ApiKey);
                },
                "Gemini is selected in Providers but Gemini:ApiKey is empty.")
            .ValidateOnStart();
        services.Configure<PrintLayoutOptions>(configuration.GetSection(PrintLayoutOptions.SectionName));
        services.Configure<BekiPrintLayoutOptions>(configuration.GetSection(BekiPrintLayoutOptions.SectionName));
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
        services.Configure<LocalBlobOptions>(configuration.GetSection(LocalBlobOptions.SectionName));
        // The same treatment, for the same reason, on the two sections that decide whether money
        // can be taken. A malformed Bog:Enabled would otherwise 500 every checkout at the moment
        // a parent presses pay, which is the worst possible place to discover a typo.
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<BogOptions>()
            .Bind(configuration.GetSection(BogOptions.SectionName))
            .ValidateOnStart();
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.SectionName));
        services.Configure<RecaptchaOptions>(configuration.GetSection(RecaptchaOptions.SectionName));
        services.Configure<PasswordlessAuthOptions>(configuration.GetSection(PasswordlessAuthOptions.SectionName));
        services.Configure<ClientIpOptions>(configuration.GetSection(ClientIpOptions.SectionName));
        services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));
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

        services.AddStoryEngine();

        services.AddHttpClient("OpenAI", (sp, client) =>
        {
            var openAi = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            client.BaseAddress = new Uri(openAi.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(6);
        });

        // A parent is waiting on the redirect while this call is out, so the gateway gets
        // half a minute rather than the default hundred seconds to answer.
        services.AddHttpClient(BogPaymentClient.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(30));

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
        services.AddScoped<IMasterStoryRunRepository, MasterStoryRunRepository>();
        services.AddScoped<IMasterBookService, MasterBookService>();
        services.AddScoped<IMasterStoryRunCleanupService, MasterStoryRunCleanupService>();
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

        // Singleton so BOG's short-lived access token is fetched once every few minutes
        // rather than once per checkout; it holds no per-request state.
        services.AddSingleton<IBogPaymentClient, BogPaymentClient>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBookFulfillmentService, BookFulfillmentService>();
        services.AddScoped<IPrintOrderService, PrintOrderService>();

        services.AddScoped<IReferenceImageNormalizer, ReferenceImageNormalizer>();

        // Which vendor answers which half of the book. Both halves default to OpenAI, and the
        // choice is made per resolution rather than at startup so that flipping the setting is a
        // restart rather than a deployment — the same shape the blob-storage switch above uses.
        services.AddScoped<OpenAiService>();
        services.AddScoped<IGeminiInteractionsClient, GeminiInteractionsClient>();
        services.AddScoped<GeminiIllustrationClient>();

        services.AddScoped<IOpenAiService>(sp =>
        {
            var openAi = sp.GetRequiredService<OpenAiService>();
            return sp.GetRequiredService<IOptions<AiProviderOptions>>().Value.UsesGeminiForImages
                ? ActivatorUtilities.CreateInstance<AiServiceRouter>(
                    sp, openAi, sp.GetRequiredService<GeminiIllustrationClient>())
                : openAi;
        });

        services.AddScoped<IAdventurePdfService, AdventurePdfService>();
        // Azure unless a machine has explicitly asked for the local folder. The choice is made
        // per resolution rather than at startup so the registration needs no IConfiguration,
        // and LocalBlobOptions.Enabled is false in every committed settings file.
        services.AddScoped<IBlobStorageService>(sp =>
            sp.GetRequiredService<IOptions<LocalBlobOptions>>().Value.Enabled
                ? ActivatorUtilities.CreateInstance<LocalFileBlobStorageService>(sp)
                : ActivatorUtilities.CreateInstance<AzureBlobStorageService>(sp));
        services.AddScoped<IAdventureGenerationService, AdventureGenerationService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IAdminNotifier, AdminNotifier>();
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
        services.AddScoped<IBekiStoryService, BekiStoryService>();
        services.AddScoped<IBekiStoryRepository, BekiStoryRepository>();
        services.AddScoped<IBekiVisualRepository, BekiVisualRepository>();

        // The composite pipeline, registered unconditionally and beside the generator that branches
        // into it. Unconditional because a flag read at registration is a flag that needs a
        // deployment to change, and because resolving this costs nothing: it holds no assets until
        // it draws something — the pose registry, the 16 MB of approved artwork and the pipeline
        // config all load on first use, which on a deployment with the flag off is never.
        services.AddScoped<ICompositeBookPipeline, CompositeBookPipeline>();

        // The Beki-format book: spread illustrator, print layout, and the fulfilment job that
        // runs them for a purchased pack when BekiOptions.Enabled routes it this way.
        services.AddScoped<IBekiBookGenerator, BekiBookGenerator>();
        services.AddScoped<IBekiPdfComposer, BekiPdfComposer>();
        services.AddScoped<IBekiPackFulfillment, BekiPackFulfillment>();

        // The intake gate is not part of a pipeline: it runs on its own, in front of everything,
        // while a parent is still on the form.
        services.AddScoped<IPortraitGate, PortraitGate>();

        return services;
    }
}
