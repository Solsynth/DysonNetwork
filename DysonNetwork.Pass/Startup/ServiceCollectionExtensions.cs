using System.Globalization;
using DysonNetwork.Pass.Account;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Auth.OpenId;
using DysonNetwork.Pass.Localization;
using DysonNetwork.Pass.Permission;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Pass.Account.Presences;
using DysonNetwork.Pass.Affiliation;
using DysonNetwork.Pass.Auth.OidcProvider.Options;
using DysonNetwork.Pass.Auth.OidcProvider.Services;
using DysonNetwork.Pass.Credit;
using DysonNetwork.Pass.Handlers;
using DysonNetwork.Pass.Leveling;
using DysonNetwork.Pass.Mailer;
using DysonNetwork.Pass.Realm;
using DysonNetwork.Pass.Rewind;
using DysonNetwork.Pass.Safety;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Shared.Localization;

namespace DysonNetwork.Pass.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.AddDbContext<AppDatabase>();
        services.AddHttpContextAccessor();

        services.AddHttpClient();

        // Register gRPC services
        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true; // Will be adjusted in Program.cs
            options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16MB
            options.MaxSendMessageSize = 16 * 1024 * 1024; // 16MB
        });
        services.AddGrpcReflection();

        services.AddRingService();

        // Register OIDC services
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

        // Configure rate limiting
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("captcha", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = 5; // 5 attempts per minute
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
        services.AddScoped<ActionLogFlushHandler>();
        services.AddScoped<LastActiveFlushHandler>();

        return services;
    }

    public static IServiceCollection AddAppBusinessServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<RazorViewRenderer>();
        services.AddSingleton<DysonNetwork.Shared.Localization.ILocalizationService, DysonNetwork.Shared.Localization.JsonLocalizationService>(sp =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNamespace = "DysonNetwork.Pass.Resources.Locales";
            return new JsonLocalizationService(assembly, resourceNamespace);
        });
        services.AddScoped<Shared.Templating.ITemplateService, Shared.Templating.DotLiquidTemplateService>(sp =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNamespace = "DysonNetwork.Pass.Resources.Templates";
            return new DysonNetwork.Shared.Templating.DotLiquidTemplateService(assembly, resourceNamespace);
        });
        services.Configure<GeoOptions>(configuration.GetSection("GeoIP"));
        services.AddScoped<GeoService>();
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
        services.AddScoped<SafetyService>();
        services.AddScoped<SocialCreditService>();
        services.AddScoped<ExperienceService>();
        services.AddScoped<RealmService>();
        services.AddScoped<AffiliationSpellService>();

        services.AddScoped<SpotifyPresenceService>();
        services.AddScoped<SteamPresenceService>();
        services.AddScoped<IPresenceService, SpotifyPresenceService>();
        services.AddScoped<IPresenceService, SteamPresenceService>();

        services.Configure<OidcProviderOptions>(configuration.GetSection("OidcProvider"));
        services.AddScoped<OidcProviderService>();

        services.AddScoped<PassRewindService>();
        services.AddScoped<AccountRewindService>();

        services.AddEventBus()
            .AddListener<WebSocketConnectedEvent>(async (evt, ctx) =>
            {
                var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                var accountEventService = ctx.ServiceProvider.GetRequiredService<AccountEventService>();
                var cache = ctx.ServiceProvider.GetRequiredService<ICacheService>();
                var nats = ctx.ServiceProvider.GetRequiredService<NATS.Client.Core.INatsConnection>();

                logger.LogInformation("Received WebSocket connected event for user {AccountId}, device {DeviceId}",
                    evt.AccountId, evt.DeviceId);

                var previous = await cache.GetAsync<SnAccountStatus>($"account:status:prev:{evt.AccountId}");
                var status = await accountEventService.GetStatus(evt.AccountId);

                if (previous != null && !BroadcastEventHandler.StatusesEqual(previous, status))
                {
                    await nats.PublishAsync(
                        AccountStatusUpdatedEvent.Type,
                        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new AccountStatusUpdatedEvent
                        {
                            AccountId = evt.AccountId,
                            Status = status,
                            UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                        }, InfraObjectCoder.SerializerOptionsWithoutIgnore)),
                        cancellationToken: ctx.CancellationToken
                    );
                }

                logger.LogInformation("Handled status update for user {AccountId} on connect", evt.AccountId);
            })
            .AddListener<WebSocketDisconnectedEvent>(async (evt, ctx) =>
            {
                var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                var accountEventService = ctx.ServiceProvider.GetRequiredService<AccountEventService>();
                var cache = ctx.ServiceProvider.GetRequiredService<ICacheService>();
                var nats = ctx.ServiceProvider.GetRequiredService<NATS.Client.Core.INatsConnection>();

                logger.LogInformation(
                    "Received WebSocket disconnected event for user {AccountId}, device {DeviceId}, IsOffline: {IsOffline}",
                    evt.AccountId, evt.DeviceId, evt.IsOffline
                );

                var previous = await cache.GetAsync<SnAccountStatus>($"account:status:prev:{evt.AccountId}");
                var status = await accountEventService.GetStatus(evt.AccountId);

                if (previous != null && !BroadcastEventHandler.StatusesEqual(previous, status))
                {
                    await nats.PublishAsync(
                        AccountStatusUpdatedEvent.Type,
                        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new AccountStatusUpdatedEvent
                        {
                            AccountId = evt.AccountId,
                            Status = status,
                            UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                        }, InfraObjectCoder.SerializerOptionsWithoutIgnore)),
                        cancellationToken: ctx.CancellationToken
                    );
                }

                logger.LogInformation("Handled status update for user {AccountId} on disconnect", evt.AccountId);
            });

        return services;
    }
}
