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
using DysonNetwork.Passport.Meet;
using DysonNetwork.Passport.Nearby;
using DysonNetwork.Passport.Nfc;
using DysonNetwork.Passport.Progression;
using DysonNetwork.Passport.Realm;
using DysonNetwork.Passport.Rewind;
using DysonNetwork.Passport.Ticket;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Pagination;
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

        services.AddControllers().AddPaginationValidationFilter().AddJsonOptions(options =>
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
        services.AddSingleton<ILocalizationService, JsonLocalizationService>(sp =>
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
        services.AddScoped<RealmQuotaService>();
        services.AddScoped<RealmExperienceService>();
        services.AddScoped<AffiliationSpellService>();
        services.AddScoped<ProgressionSeedService>();
        services.AddScoped<ProgressionService>();

        services.AddScoped<SteamPresenceService>();
        services.AddScoped<IPresenceService, SteamPresenceService>();
        
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

        services.AddScoped<PassRewindService>();
        services.AddScoped<AccountRewindService>();
        services.AddScoped<TicketService>();
        services.AddScoped<MeetService>();
        services.AddScoped<NearbyService>();
        services.AddScoped<NfcService>();
        services.AddSingleton<MeetSubscriptionHub>();
        services.AddSingleton<MeetExpirationScheduler>();
        services.AddHostedService<MeetLifecycleHostedService>();
        services.AddScoped<LocationPinService>();
        services.AddSingleton<LocationPinSubscriptionHub>();
        services.AddHostedService<LocationPinLifecycleHostedService>();
        services.AddEventBus()
            .AddListener<AccountCreatedEvent>(async (evt, ctx) =>
            {
                var spells = ctx.ServiceProvider.GetRequiredService<MagicSpellService>();
                var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                
                logger.LogInformation("Handling account creation event for @{UserName}", evt.Name);

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
            })
            .AddListener<PaymentOrderEvent>(
                PaymentOrderEventBase.Type,
                async (evt, ctx) =>
                {
                    if (evt.ProductIdentifier != "realms.boost") return;

                    var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                    var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();
                    var experience = ctx.ServiceProvider.GetRequiredService<RealmExperienceService>();

                    var boostEvt = JsonSerializer.Deserialize<PaymentOrderRealmBoostEvent>(
                        JsonSerializer.Serialize(evt, InfraObjectCoder.SerializerOptions),
                        InfraObjectCoder.SerializerOptions
                    );
                    if (boostEvt?.Meta == null) return;

                    if (await db.RealmBoostContributions.AnyAsync(c => c.OrderId == boostEvt.OrderId, ctx.CancellationToken))
                    {
                        logger.LogDebug("Skipping duplicate realm boost order {OrderId}", boostEvt.OrderId);
                        return;
                    }

                    var realm = await db.Realms.FirstOrDefaultAsync(r => r.Id == boostEvt.Meta.RealmId, ctx.CancellationToken);
                    if (realm is null) return;

                    var currency = RealmBoostPolicy.NormalizeCurrency(boostEvt.Meta.Currency);
                    var amount = decimal.Parse(boostEvt.Meta.Amount, CultureInfo.InvariantCulture);
                    db.RealmBoostContributions.Add(new SnRealmBoostContribution
                    {
                        RealmId = boostEvt.Meta.RealmId,
                        AccountId = boostEvt.Meta.AccountId,
                        Currency = currency,
                        Amount = amount,
                        OrderId = boostEvt.OrderId,
                        TransactionId = Guid.Empty
                    });

                    var cutoff = RealmBoostPolicy.GetActiveCutoff(SystemClock.Instance.GetCurrentInstant());
                    var activeContributions = await db.RealmBoostContributions
                        .Where(c => c.RealmId == boostEvt.Meta.RealmId && c.CreatedAt >= cutoff)
                        .ToListAsync(ctx.CancellationToken);
                    realm.BoostPoints = activeContributions.Sum(c => c.Shares) +
                        RealmBoostPolicy.GetSharesForAmount(currency, amount);
                    await db.SaveChangesAsync(ctx.CancellationToken);

                    var boostXp = boostEvt.Meta.Shares * RealmExperienceService.BoostShareXp;
                    var xpRecord = await experience.AddRecord(
                        boostEvt.Meta.RealmId,
                        boostEvt.Meta.AccountId,
                        "realm.boost.purchase",
                        boostEvt.OrderId.ToString(),
                        boostXp,
                        cooldown: null,
                        cancellationToken: ctx.CancellationToken
                    );

                    if (xpRecord is not null)
                    {
                        logger.LogDebug(
                            "Granted realm boost XP for order {OrderId} to {RealmId}/{AccountId}",
                            boostEvt.OrderId,
                            boostEvt.Meta.RealmId,
                            boostEvt.Meta.AccountId
                        );
                    }
                },
                opts =>
                {
                    opts.UseJetStream = true;
                    opts.StreamName = "payment_events";
                    opts.ConsumerName = "passport_realm_boost_orders";
                    opts.MaxRetries = 3;
                })
            .AddListener<RealmActivityEvent>(
                RealmActivityEvent.SubjectPrefix + ">",
                async (evt, ctx) =>
                {
                    var experience = ctx.ServiceProvider.GetRequiredService<RealmExperienceService>();
                    var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();

                    var cooldown = evt.ActivityType == "chat_message"
                        ? NodaTime.Duration.FromMinutes(1)
                        : (NodaTime.Duration?)null;

                    var record = await experience.AddRecord(
                        evt.RealmId,
                        evt.AccountId,
                        $"realm.activity.{evt.ActivityType}",
                        evt.ReferenceId,
                        evt.Delta,
                        cooldown,
                        ctx.CancellationToken
                    );

                    if (record is not null)
                        logger.LogDebug("Recorded realm activity XP {ActivityType} for {RealmId}/{AccountId}", evt.ActivityType, evt.RealmId, evt.AccountId);
                },
                opts =>
                {
                    opts.UseJetStream = true;
                    opts.StreamName = "realm_activity_events";
                    opts.ConsumerName = "passport_realm_activity_xp";
                    opts.MaxRetries = 3;
                })
            .AddListener<ActionLogTriggeredEvent>(
                ActionLogTriggeredEvent.SubjectPrefix + ">",
                async (evt, ctx) =>
                {
                    var progression = ctx.ServiceProvider.GetRequiredService<ProgressionService>();
                    await progression.HandleActionLogAsync(evt, ctx.CancellationToken);
                },
                opts =>
                {
                    opts.UseJetStream = true;
                    opts.StreamName = "action_log_events";
                    opts.ConsumerName = "passport_progression_action_logs";
                    opts.MaxRetries = 3;
                })
            .AddListener<LocationPinUpdatedEvent>(
                LocationPinUpdatedEvent.SubjectPrefix + ">",
                async (evt, ctx) =>
                {
                    var subscriptions = ctx.ServiceProvider.GetRequiredService<LocationPinSubscriptionHub>();
                    var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();

                    var pin = await db.LocationPins.FirstOrDefaultAsync(p => p.Id == evt.PinId, ctx.CancellationToken);
                    if (pin == null) return;

                    pin.Account = ctx.ServiceProvider.GetRequiredService<AccountService>().GetAccount(pin.AccountId).Result;

                    await subscriptions.PublishAsync("pin_updated", pin, ctx.CancellationToken);
                },
                opts =>
                {
                    opts.UseJetStream = true;
                    opts.StreamName = "locationpin_events";
                    opts.ConsumerName = "passport_locationpin_updates";
                    opts.MaxRetries = 3;
                });

        return services;
    }
}
