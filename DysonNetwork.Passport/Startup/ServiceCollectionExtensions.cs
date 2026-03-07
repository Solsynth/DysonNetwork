using System.Globalization;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Passport.Account;
using DysonNetwork.Passport.Account.Presences;
using DysonNetwork.Passport.Affiliation;
using DysonNetwork.Passport.Auth;
using DysonNetwork.Passport.Auth.OidcProvider.Options;
using DysonNetwork.Passport.Auth.OidcProvider.Services;
using DysonNetwork.Passport.Auth.OpenId;
using DysonNetwork.Passport.Credit;
using DysonNetwork.Passport.Handlers;
using DysonNetwork.Passport.Leveling;
using DysonNetwork.Passport.Localization;
using DysonNetwork.Passport.Mailer;
using DysonNetwork.Passport.Permission;
using DysonNetwork.Passport.Realm;
using DysonNetwork.Passport.Rewind;
using DysonNetwork.Passport.Safety;
using DysonNetwork.Passport.Ticket;
using DysonNetwork.Shared.Auth;
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
using Npgsql;

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
        services.AddSingleton<DysonNetwork.Shared.Localization.ILocalizationService, DysonNetwork.Shared.Localization.JsonLocalizationService>(sp =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNamespace = "DysonNetwork.Passport.Resources.Locales";
            return new JsonLocalizationService(assembly, resourceNamespace);
        });
        services.AddScoped<Shared.Templating.ITemplateService, Shared.Templating.DotLiquidTemplateService>(sp =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNamespace = "DysonNetwork.Passport.Resources.Templates";
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
        services.AddScoped<RelationshipService>();
        services.AddScoped<MagicSpellService>();
        services.AddSingleton<AuthTokenKeyProvider>();
        services.AddScoped<AuthService>();
        services.AddScoped<TokenAuthService>();
        services.AddScoped<AccountUsernameService>();
        services.AddScoped<SafetyService>();
        services.AddScoped<SocialCreditService>();
        services.AddScoped<ExperienceService>();
        services.AddScoped<RealmService>();
        services.AddScoped<AffiliationSpellService>();
        services.AddGrpcClientWithSharedChannel<DyAccountService.DyAccountServiceClient>(
            "https://_grpc.padlock",
            "DyAccountService.Padlock");
        services.AddGrpcClientWithSharedChannel<DyActionLogService.DyActionLogServiceClient>(
            "https://_grpc.padlock",
            "DyActionLogService.Padlock");
        services.AddSingleton<RemoteActionLogService>();
        services.AddScoped<PadlockAccountContactService>();

        services.AddScoped<SpotifyPresenceService>();
        services.AddScoped<SteamPresenceService>();
        services.AddScoped<IPresenceService, SpotifyPresenceService>();
        services.AddScoped<IPresenceService, SteamPresenceService>();

        services.Configure<OidcProviderOptions>(configuration.GetSection("OidcProvider"));
        services.AddScoped<OidcProviderService>();

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

                var changed = false;
                var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == evt.AccountId, ctx.CancellationToken);
                if (account is null)
                {
                    account = new SnAccount
                    {
                        Id = evt.AccountId,
                        Name = evt.Name,
                        Nick = evt.Nick,
                        Language = evt.Language,
                        Region = evt.Region,
                        ActivatedAt = evt.ActivatedAt,
                        IsSuperuser = evt.IsSuperuser
                    };
                    db.Accounts.Add(account);
                    changed = true;
                }

                var profileExists = await db.AccountProfiles
                    .AnyAsync(p => p.AccountId == evt.AccountId, ctx.CancellationToken);
                if (!profileExists)
                {
                    db.AccountProfiles.Add(new SnAccountProfile
                    {
                        AccountId = evt.AccountId
                    });
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(evt.PrimaryEmail))
                {
                    var contact = await db.AccountContacts
                        .FirstOrDefaultAsync(
                            c => c.AccountId == evt.AccountId && c.Type == AccountContactType.Email &&
                                 c.Content == evt.PrimaryEmail,
                            ctx.CancellationToken);
                    if (contact is null)
                    {
                        db.AccountContacts.Add(new SnAccountContact
                        {
                            AccountId = evt.AccountId,
                            Type = AccountContactType.Email,
                            Content = evt.PrimaryEmail!,
                            IsPrimary = true,
                            VerifiedAt = evt.PrimaryEmailVerifiedAt
                        });
                        changed = true;
                    }
                    else
                    {
                        var contactChanged = false;
                        if (!contact.IsPrimary)
                        {
                            contact.IsPrimary = true;
                            contactChanged = true;
                        }
                        if (evt.PrimaryEmailVerifiedAt is not null && contact.VerifiedAt is null)
                        {
                            contact.VerifiedAt = evt.PrimaryEmailVerifiedAt;
                            contactChanged = true;
                        }
                        if (contactChanged)
                        {
                            db.AccountContacts.Update(contact);
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    try
                    {
                        await db.SaveChangesAsync(ctx.CancellationToken);
                    }
                    catch (DbUpdateException ex) when (IsUniqueViolation(ex, "pk_accounts"))
                    {
                        // Concurrent duplicate account creation event across instances; ignore and continue.
                        db.ChangeTracker.Clear();
                        account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == evt.AccountId, ctx.CancellationToken);
                    }
                }

                if (account is not null && account.ActivatedAt is null && !string.IsNullOrWhiteSpace(evt.PrimaryEmail))
                {
                    var spell = await spells.CreateMagicSpell(
                        account,
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

    private static bool IsUniqueViolation(DbUpdateException ex, string constraintName)
    {
        return ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: var constraint
        } && string.Equals(constraint, constraintName, StringComparison.OrdinalIgnoreCase);
    }
}
