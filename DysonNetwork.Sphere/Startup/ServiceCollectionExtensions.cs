using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Pagination;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Sphere.ActivityPub;
using DysonNetwork.Sphere.ActivityPub.Services;
using DysonNetwork.Sphere.Autocompletion;
using DysonNetwork.Sphere.Poll;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Sticker;
using DysonNetwork.Sphere.Timeline;
using DysonNetwork.Sphere.Fitness;
using DysonNetwork.Sphere.Translation;
using DysonNetwork.Sphere.Live;
using DysonNetwork.Sphere.Automod;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Sphere.Startup;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddAppServices()
        {
            services.AddLocalization(options => options.ResourcesPath = "Resources");

            services.AddDbContext<AppDatabase>();
            services.AddHttpContextAccessor();

            services.AddHttpClient();

            services
                .AddControllers()
                .AddPaginationValidationFilter()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.NumberHandling =
                        JsonNumberHandling.AllowNamedFloatingPointLiterals;
                    options.JsonSerializerOptions.PropertyNamingPolicy =
                        JsonNamingPolicy.SnakeCaseLower;

                    options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
                });
            services.AddRazorPages();

            services.AddGrpc(options =>
            {
                options.EnableDetailedErrors = true;
                options.MaxReceiveMessageSize = 100 * 1024 * 1024;
                options.MaxSendMessageSize = 100 * 1024 * 1024;
            });
            services.AddGrpcReflection();

            services.Configure<RequestLocalizationOptions>(options =>
            {
                var supportedCultures = new[] { new CultureInfo("en-US"), new CultureInfo("zh-Hans") };

                options.SupportedCultures = supportedCultures;
                options.SupportedUICultures = supportedCultures;
            });

            services.AddHostedService<ActivityPubDeliveryWorker>();

            services.AddEventBus()
                .AddListener<PaymentOrderEvent>(
                    PaymentOrderEventBase.Type,
                    async (evt, ctx) =>
                    {
                        if (evt?.ProductIdentifier is null) return;

                        var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();

                        logger.LogInformation(
                            "Received order event: {ProductIdentifier} {OrderId}",
                            evt.ProductIdentifier,
                            evt.OrderId
                        );

                        switch (evt.ProductIdentifier)
                        {
                            case "posts.award":
                            {
                                var awardEvt = JsonSerializer.Deserialize<PaymentOrderAwardEvent>(
                                    JsonSerializer.Serialize(evt, InfraObjectCoder.SerializerOptions),
                                    InfraObjectCoder.SerializerOptions
                                );
                                if (awardEvt?.Meta == null) throw new ArgumentNullException(nameof(awardEvt));

                                var meta = awardEvt.Meta;

                                logger.LogInformation("Handling post award order: {OrderId}", evt.OrderId);

                                var ps = ctx.ServiceProvider.GetRequiredService<PostService>();
                                var amountNum = decimal.Parse(meta.Amount);

                                await ps.AwardPost(meta.PostId, meta.AccountId, amountNum, meta.Attitude, meta.Message);

                                logger.LogInformation("Post award for order {OrderId} handled successfully.",
                                    evt.OrderId);
                                break;
                            }
                            case "livestreams.award":
                            {
                                var awardEvt = JsonSerializer.Deserialize<PaymentOrderLiveStreamAwardEvent>(
                                    JsonSerializer.Serialize(evt, InfraObjectCoder.SerializerOptions),
                                    InfraObjectCoder.SerializerOptions
                                );
                                if (awardEvt?.Meta == null) throw new ArgumentNullException(nameof(awardEvt));

                                var meta = awardEvt.Meta;

                                logger.LogInformation("Handling livestream award order: {OrderId}", evt.OrderId);

                                var lss = ctx.ServiceProvider.GetRequiredService<LiveStreamService>();
                                var amountNum = decimal.Parse(meta.Amount);

                                await lss.ConfirmAwardAsync(meta.LiveStreamId, meta.AccountId, amountNum, meta.Message);

                                logger.LogInformation("Livestream award for order {OrderId} handled successfully.",
                                    evt.OrderId);
                                break;
                            }
                        }
                    },
                    opts =>
                    {
                        opts.UseJetStream = true;
                        // StreamName is auto-detected from event class
                        opts.MaxRetries = 3;
                    }
                )
                .AddListener<AccountDeletedEvent>(
                    AccountDeletedEvent.Type,
                    async (evt, ctx) =>
                    {
                        var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                        var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();

                        logger.LogWarning("Account deleted: {AccountId}", evt.AccountId);

                        await using var transaction = await db.Database.BeginTransactionAsync(ctx.CancellationToken);
                        try
                        {
                            var publishers = await db.Publishers
                                .Where(p => p.Members.All(m => m.AccountId == evt.AccountId))
                                .ToListAsync(ctx.CancellationToken);

                            var now = new Instant();
                            foreach (var publisher in publishers)
                                await db.Posts
                                    .Where(p => p.PublisherId == publisher.Id)
                                    .ExecuteUpdateAsync(p => p.SetProperty(s => s.DeletedAt, now),
                                        ctx.CancellationToken);

                            var publisherIds = publishers.Select(p => p.Id).ToList();
                            await db.Publishers
                                .Where(p => publisherIds.Contains(p.Id))
                                .ExecuteUpdateAsync(p => p.SetProperty(s => s.DeletedAt, now), ctx.CancellationToken);

                            await transaction.CommitAsync(ctx.CancellationToken);
                        }
                        catch
                        {
                            await transaction.RollbackAsync(ctx.CancellationToken);
                            throw;
                        }
                    },
                    opts =>
                    {
                        opts.UseJetStream = true;
                        // StreamName is auto-detected from event class
                        opts.MaxRetries = 3;
                    }
                );

            return services;
        }

        public IServiceCollection AddAppAuthentication()
        {
            services.AddAuthorization();
            return services;
        }

        public IServiceCollection AddAppFlushHandlers()
        {
            services.AddSingleton<FlushBufferService>();
            services.AddScoped<PostViewFlushHandler>();
            services.AddScoped<PostInterestFlushHandler>();

            return services;
        }

        public IServiceCollection AddAppBusinessServices(IConfiguration configuration
        )
        {
            _ = services
                .AddSingleton<DysonNetwork.Shared.Localization.ILocalizationService,
                    DysonNetwork.Shared.Localization.JsonLocalizationService>(sp =>
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var resourceNamespace = "DysonNetwork.Sphere.Resources.Locales";
                    return new Shared.Localization.JsonLocalizationService(assembly, resourceNamespace);
                });

            services.Configure<GeoOptions>(configuration.GetSection("GeoIP"));
            services.Configure<ActivityPubDeliveryOptions>(configuration.GetSection("ActivityPubDelivery"));
            services.AddScoped<GeoService>();
            services.AddScoped<PublisherService>();
            services.AddScoped<PublisherQuotaService>();
            services.AddScoped<PublisherSubscriptionService>();
            services.AddScoped<PublisherRatingService>();
            services.AddScoped<PublisherLeaderboardService>();
            services.AddScoped<TimelineService>();
            services.AddScoped<AutomodService>();
            services.AddScoped<PostService>();
            services.AddScoped<PostTagService>();
            services.AddScoped<PollService>();
            services.AddScoped<StickerService>();
            services.AddScoped<AutocompletionService>();
            services.AddScoped<ActivityPubKeyService>();
            services.AddScoped<IKeyService, ActivityPubKeyService>();
            services.AddSingleton<IServerSigningKeyService, ServerSigningKeyService>();
            services.AddScoped<ISignatureService, ActivityPubSignatureService>();
            services.AddScoped<ActivityHandlerService>();
            services.AddScoped<ActivityPubDeliveryService>();
            services.AddScoped<IActorDiscoveryService, ActivityPubDiscoveryService>();
            services.AddScoped<ActivityRenderer>();
            services.AddScoped<FediverseCachingService>();
            services.AddScoped<FediverseModerationService>();
            services.AddSingleton<ActivityPubQueueService>();
            services.AddSingleton<DeliveryBatchService>();
            services.AddSingleton<DeliveryMetricsService>();
            services.AddScoped<DeliveryDeadLetterService>();
            services.AddScoped<KeyMigrationService>();
            services.AddSingleton<IFederationMetricsService, FederationMetricsService>();
            services.AddSingleton<OutboxBackfillService>();
            services.AddScoped<LiveStreamService>();
            services.AddSingleton<LiveKitLivestreamService>();

            var translationProvider = configuration["Translation:Provider"]?.ToLower();
            switch (translationProvider)
            {
                case "tencent":
                    services.AddScoped<ITranslationProvider, TencentTranslation>();
                    break;
            }

            return services;
        }
    }
}
