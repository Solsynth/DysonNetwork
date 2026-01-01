using DysonNetwork.Insight.Reader;
using DysonNetwork.Sphere.ActivityPub;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;

using Quartz;

namespace DysonNetwork.Sphere.Startup;

public static class ScheduledJobsConfiguration
{
    public static IServiceCollection AddAppScheduledJobs(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            q.AddJob<AppDatabaseRecyclingJob>(opts => opts.WithIdentity("AppDatabaseRecycling"));
            q.AddTrigger(opts => opts
                .ForJob("AppDatabaseRecycling")
                .WithIdentity("AppDatabaseRecyclingTrigger")
                .WithCronSchedule("0 0 0 * * ?"));

            q.AddJob<PostViewFlushJob>(opts => opts.WithIdentity("PostViewFlush"));
            q.AddTrigger(opts => opts
                .ForJob("PostViewFlush")
                .WithIdentity("PostViewFlushTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(1)
                    .RepeatForever())
            );

            q.AddJob<WebFeedScraperJob>(opts => opts.WithIdentity("WebFeedScraper").StoreDurably());
            q.AddTrigger(opts => opts
                .ForJob("WebFeedScraper")
                .WithIdentity("WebFeedScraperTrigger")
                .WithCronSchedule("0 0 0 * * ?")
            );

            q.AddJob<PublisherSettlementJob>(opts => opts.WithIdentity("PublisherSettlement"));
            q.AddTrigger(opts => opts
                .ForJob("PublisherSettlement")
                .WithIdentity("PublisherSettlementTrigger")
                .WithCronSchedule("0 0 0 * * ?")
            );

            q.AddJob<ActivityPubDeliveryRetryJob>(opts => opts.WithIdentity("ActivityPubDeliveryRetry"));
            q.AddTrigger(opts => opts
                .ForJob("ActivityPubDeliveryRetry")
                .WithIdentity("ActivityPubDeliveryRetryTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(1)
                    .RepeatForever())
            );

            q.AddJob<ActivityPubDeliveryCleanupJob>(opts => opts.WithIdentity("ActivityPubDeliveryCleanup"));
            q.AddTrigger(opts => opts
                .ForJob("ActivityPubDeliveryCleanup")
                .WithIdentity("ActivityPubDeliveryCleanupTrigger")
                .WithCronSchedule("0 0 3 * * ?")
            );
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
