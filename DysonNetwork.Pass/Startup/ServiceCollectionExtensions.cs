using System.Globalization;
using DysonNetwork.Pass.Account;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Auth.OpenId;
using DysonNetwork.Pass.Email;
using DysonNetwork.Pass.Localization;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Pass.Wallet;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DysonNetwork.Pass.Auth.OidcProvider.Options;
using DysonNetwork.Pass.Auth.OidcProvider.Services;
using DysonNetwork.Pass.Credit;
using DysonNetwork.Pass.Handlers;
using DysonNetwork.Pass.Leveling;
using DysonNetwork.Pass.Safety;
using DysonNetwork.Pass.Wallet.PaymentHandlers;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.GeoIp;
using DysonNetwork.Shared.Registry;

namespace DysonNetwork.Pass.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.AddDbContext<AppDatabase>();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connection = configuration.GetConnectionString("FastRetrieve")!;
            return ConnectionMultiplexer.Connect(connection);
        });
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddHttpContextAccessor();
        services.AddSingleton<ICacheService, CacheServiceRedis>();

        services.AddHttpClient();

        // Register gRPC services
        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true; // Will be adjusted in Program.cs
            options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16MB
            options.MaxSendMessageSize = 16 * 1024 * 1024; // 16MB
        });

        services.AddPusherService();

        // Register OIDC services
        services.AddScoped<OidcService, GoogleOidcService>();
        services.AddScoped<OidcService, AppleOidcService>();
        services.AddScoped<OidcService, GitHubOidcService>();
        services.AddScoped<OidcService, MicrosoftOidcService>();
        services.AddScoped<OidcService, DiscordOidcService>();
        services.AddScoped<OidcService, AfdianOidcService>();
        services.AddScoped<GoogleOidcService>();
        services.AddScoped<AppleOidcService>();
        services.AddScoped<GitHubOidcService>();
        services.AddScoped<MicrosoftOidcService>();
        services.AddScoped<DiscordOidcService>();
        services.AddScoped<AfdianOidcService>();

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

            options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        }).AddDataAnnotationsLocalization(options =>
        {
            options.DataAnnotationLocalizerProvider = (type, factory) =>
                factory.Create(typeof(SharedResource));
        });
        services.AddRazorPages();

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new[]
            {
                new CultureInfo("en-US"),
                new CultureInfo("zh-Hans"),
            };

            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });

        return services;
    }

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o => o.AddFixedWindowLimiter(policyName: "fixed", opts =>
        {
            opts.Window = TimeSpan.FromMinutes(1);
            opts.PermitLimit = 120;
            opts.QueueLimit = 2;
            opts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        }));

        return services;
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddCors();
        services.AddAuthorization();
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AuthConstants.SchemeName;
                options.DefaultChallengeScheme = AuthConstants.SchemeName;
            })
            .AddScheme<DysonTokenAuthOptions, DysonTokenAuthHandler>(AuthConstants.SchemeName, _ => { });

        return services;
    }

    public static IServiceCollection AddAppSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Version = "v1",
                Title = "Dyson Pass",
                Description =
                    "The authentication service of the Dyson Network. Mainly handling authentication and authorization.",
                TermsOfService = new Uri("https://solsynth.dev/terms"),
                License = new OpenApiLicense
                {
                    Name = "APGLv3",
                    Url = new Uri("https://www.gnu.org/licenses/agpl-3.0.html")
                }
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "Please enter a valid token",
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                BearerFormat = "JWT",
                Scheme = "Bearer"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    []
                }
            });
        });
        services.AddOpenApi();

        return services;
    }

    public static IServiceCollection AddAppFlushHandlers(this IServiceCollection services)
    {
        services.AddSingleton<FlushBufferService>();
        services.AddScoped<ActionLogFlushHandler>();
        services.AddScoped<LastActiveFlushHandler>();

        return services;
    }

    public static IServiceCollection AddAppBusinessServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<CompactTokenService>();
        services.AddScoped<RazorViewRenderer>();
        services.Configure<GeoIpOptions>(configuration.GetSection("GeoIP"));
        services.AddScoped<GeoIpService>();
        services.AddScoped<EmailService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<ActionLogService>();
        services.AddScoped<AccountService>();
        services.AddScoped<AccountEventService>();
        services.AddScoped<NotableDaysService>();
        services.AddScoped<ActionLogService>();
        services.AddScoped<RelationshipService>();
        services.AddScoped<MagicSpellService>();
        services.AddScoped<AuthService>();
        services.AddScoped<TokenAuthService>();
        services.AddScoped<AccountUsernameService>();
        services.AddScoped<WalletService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<AfdianPaymentHandler>();
        services.AddScoped<SafetyService>();
        services.AddScoped<SocialCreditService>();
        services.AddScoped<ExperienceService>();
        
        services.Configure<OidcProviderOptions>(configuration.GetSection("OidcProvider"));
        services.AddScoped<OidcProviderService>();
        
        services.AddHostedService<BroadcastEventHandler>();

        return services;
    }
}