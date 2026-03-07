using System.Globalization;
using DysonNetwork.Padlock.Auth;
using DysonNetwork.Padlock.Auth.OpenId;
using DysonNetwork.Padlock.Account;
using DysonNetwork.Padlock.Permission;
using DysonNetwork.Padlock.Localization;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Padlock.Auth.OidcProvider.Options;
using DysonNetwork.Padlock.Auth.OidcProvider.Services;
using DysonNetwork.Padlock.E2EE;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Queue;

namespace DysonNetwork.Padlock.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.AddDbContext<AppDatabase>();
        services.AddHttpContextAccessor();

        services.AddHttpClient();

        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true;
            options.MaxReceiveMessageSize = 16 * 1024 * 1024;
            options.MaxSendMessageSize = 16 * 1024 * 1024;
        });
        services.AddGrpcReflection();

        services.AddScoped<OidcService, GoogleOidcService>();
        services.AddScoped<OidcService, AppleOidcService>();
        services.AddScoped<OidcService, GitHubOidcService>();
        services.AddScoped<OidcService, MicrosoftOidcService>();
        services.AddScoped<OidcService, DiscordOidcService>();
        services.AddScoped<OidcService, AfdianOidcService>();
        services.AddScoped<OidcService, SpotifyOidcService>();
        services.AddScoped<OidcService, SteamOidcService>();
        services.AddScoped<GoogleOidcService>();
        services.AddScoped<AppleOidcService>();
        services.AddScoped<GitHubOidcService>();
        services.AddScoped<MicrosoftOidcService>();
        services.AddScoped<DiscordOidcService>();
        services.AddScoped<AfdianOidcService>();
        services.AddScoped<SpotifyOidcService>();
        services.AddScoped<SteamOidcService>();

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

        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("captcha", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = 5;
                opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                opt.QueueLimit = 2;
            });
        });

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

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AuthConstants.SchemeName;
                options.DefaultChallengeScheme = AuthConstants.SchemeName;
            })
            .AddScheme<DysonTokenAuthOptions, DysonTokenAuthHandler>(AuthConstants.SchemeName, _ => { });

        return services;
    }

    public static IServiceCollection AddAppFlushHandlers(this IServiceCollection services)
    {
        services.AddSingleton<FlushBufferService>();

        return services;
    }

    public static IServiceCollection AddAppBusinessServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<DysonNetwork.Shared.Localization.ILocalizationService, DysonNetwork.Shared.Localization.JsonLocalizationService>(sp =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNamespace = "DysonNetwork.Padlock.Resources.Locales";
            return new JsonLocalizationService(assembly, resourceNamespace);
        });
        services.AddScoped<Shared.Templating.ITemplateService, Shared.Templating.DotLiquidTemplateService>(sp =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNamespace = "DysonNetwork.Padlock.Resources.Templates";
            return new DysonNetwork.Shared.Templating.DotLiquidTemplateService(assembly, resourceNamespace);
        });
        services.Configure<GeoOptions>(configuration.GetSection("GeoIP"));
        services.AddScoped<GeoService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<AccountService>();
        services.AddSingleton<AuthTokenKeyProvider>();
        services.AddSingleton<AuthJwtService>();
        services.AddScoped<AuthService>();
        services.AddScoped<TokenAuthService>();
        services.AddScoped<E2EeService>();
        services.AddScoped<IE2eeModule>(sp => sp.GetRequiredService<E2EeService>());
        services.AddScoped<IGroupE2eeModule>(sp => sp.GetRequiredService<E2EeService>());

        services.Configure<OidcProviderOptions>(configuration.GetSection("OidcProvider"));
        services.AddScoped<OidcProviderService>();
        services.AddEventBus()
            .AddListener<AccountActivatedEvent>(async (evt, ctx) =>
            {
                var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                var accounts = ctx.ServiceProvider.GetRequiredService<AccountService>();

                var handled = await accounts.ActivateAccountAndGrantDefaultPermissions(evt.AccountId, evt.ActivatedAt);
                if (!handled)
                {
                    logger.LogWarning("Received activation event for missing account {AccountId}", evt.AccountId);
                    return;
                }

                logger.LogInformation("Applied activation event for account {AccountId}", evt.AccountId);
            });

        return services;
    }
}
