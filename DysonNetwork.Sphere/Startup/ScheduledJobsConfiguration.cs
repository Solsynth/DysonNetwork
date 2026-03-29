using DysonNetwork.Sphere.ActivityPub;
using DysonNetwork.Sphere.Live;
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

            q.AddJob<PostInterestFlushJob>(opts => opts.WithIdentity("PostInterestFlush"));
            q.AddTrigger(opts => opts
                .ForJob("PostInterestFlush")
                .WithIdentity("PostInterestFlushTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(1)
                    .RepeatForever())
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

            q.AddJob<FediverseActorCleanupJob>(opts => opts.WithIdentity("FediverseActorCleanup"));
            q.AddTrigger(opts => opts
                .ForJob("FediverseActorCleanup")
                .WithIdentity("FediverseActorCleanupTrigger")
                .WithCronSchedule("0 0 4 * * ?")
            );

            q.AddJob<LiveStreamIngressCleanupJob>(opts => opts.WithIdentity("LiveStreamIngressCleanup"));
            q.AddTrigger(opts => opts
                .ForJob("LiveStreamIngressCleanup")
                .WithIdentity("LiveStreamIngressCleanupTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(5)
                    .RepeatForever())
            );

            q.AddJob<PublisherFollowRequestCleanupJob>(opts => opts.WithIdentity("PublisherFollowRequestCleanup"));
            q.AddTrigger(opts => opts
                .ForJob("PublisherFollowRequestCleanup")
                .WithIdentity("PublisherFollowRequestCleanupTrigger")
                .WithCronSchedule("0 0 6 * * ?")
            );
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
