using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.WebReader;
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
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
