using System.Globalization;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Passport.Account;
using DysonNetwork.Passport.Account.Presences;
using DysonNetwork.Passport.Affiliation;
using DysonNetwork.Passport.Credit;
using DysonNetwork.Passport.Handlers;
using DysonNetwork.Passport.Leveling;
using DysonNetwork.Passport.Mailer;
using DysonNetwork.Passport.Realm;
using DysonNetwork.Passport.Rewind;
using DysonNetwork.Passport.Ticket;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Shared.Localization;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Passport.Startup;

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

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

            options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        });

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

        return services;
    }

    public static IServiceCollection AddAppFlushHandlers(this IServiceCollection services)
    {
        services.AddSingleton<FlushBufferService>();
        services.AddScoped<LastActiveFlushHandler>();

        return services;
    }

    public static IServiceCollection AddAppBusinessServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<RazorViewRenderer>();
        services.AddSingleton<ILocalizationService, DysonNetwork.Shared.Localization.JsonLocalizationService>(sp =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNamespace = "DysonNetwork.Passport.Resources.Locales";
            return new JsonLocalizationService(assembly, resourceNamespace);
        });
        services.AddScoped<Shared.Templating.ITemplateService, Shared.Templating.DotLiquidTemplateService>(sp =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNamespace = "DysonNetwork.Passport.Resources.Templates";
            return new Shared.Templating.DotLiquidTemplateService(assembly, resourceNamespace);
        });
        services.Configure<GeoOptions>(configuration.GetSection("GeoIP"));
        services.AddScoped<GeoService>();
        services.AddScoped<EmailService>();
        services.AddScoped<ActionLogService>();
        services.AddScoped<AccountService>();
        services.AddScoped<AccountEventService>();
        services.AddScoped<NotableDaysService>();
        services.AddScoped<RelationshipService>();
        services.AddScoped<MagicSpellService>();
        services.AddScoped<AccountUsernameService>();
        services.AddScoped<SocialCreditService>();
        services.AddScoped<ExperienceService>();
        services.AddScoped<RealmService>();
        services.AddScoped<AffiliationSpellService>();
        services.AddGrpcClientWithSharedChannel<DyAccountService.DyAccountServiceClient>(
            "https://_grpc.padlock",
            "DyAccountService");
        services.AddGrpcClientWithSharedChannel<DyProfileService.DyProfileServiceClient>(
            "https://_grpc.passport",
            "DyProfileService");
        services.AddScoped<RemoteAccountContactService>();
        services.AddScoped<RemoteAccountConnectionService>();
        services.AddGrpcClientWithSharedChannel<DyActionLogService.DyActionLogServiceClient>(
            "https://_grpc.padlock",
            "DyActionLogService");
        services.AddSingleton<RemoteActionLogService>();

        services.AddScoped<SpotifyPresenceService>();
        services.AddScoped<SteamPresenceService>();
        services.AddScoped<IPresenceService, SpotifyPresenceService>();
        services.AddScoped<IPresenceService, SteamPresenceService>();

        services.AddScoped<PassRewindService>();
        services.AddScoped<AccountRewindService>();
        services.AddScoped<TicketService>();
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
            })
            .AddListener<AccountCreatedEvent>(async (evt, ctx) =>
            {
                var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();
                var spells = ctx.ServiceProvider.GetRequiredService<MagicSpellService>();
                var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                
                logger.LogInformation("Handling account creation event for @{UserName}", evt.Name);

                var profileExists = await db.AccountProfiles
                    .AnyAsync(p => p.AccountId == evt.AccountId, ctx.CancellationToken);
                if (!profileExists)
                {
                    db.AccountProfiles.Add(new SnAccountProfile
                    {
                        AccountId = evt.AccountId
                    });
                    await db.SaveChangesAsync(ctx.CancellationToken);
                }

                if (evt.ActivatedAt is null && !string.IsNullOrWhiteSpace(evt.PrimaryEmail))
                {
                    var spell = await spells.CreateMagicSpell(
                        new SnAccount
                        {
                            Id = evt.AccountId,
                            Name = evt.Name,
                            Nick = evt.Nick,
                            Language = evt.Language,
                            Region = evt.Region
                        },
                        MagicSpellType.AccountActivation,
                        new Dictionary<string, object>
                        {
                            { "contact_method", evt.PrimaryEmail! }
                        },
                        preventRepeat: true
                    );
                    await spells.NotifyMagicSpell(spell, true);
                }

                logger.LogInformation("Handled account created event for {AccountId}", evt.AccountId);
            });

        return services;
    }
}
