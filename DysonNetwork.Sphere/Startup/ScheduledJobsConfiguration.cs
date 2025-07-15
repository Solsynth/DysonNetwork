using DysonNetwork.Sphere.WebReader;
using Quartz;

namespace DysonNetwork.Sphere.Startup;

public static class ScheduledJobsConfiguration
{
    public static IServiceCollection AddAppScheduledJobs(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            var appDatabaseRecyclingJob = new JobKey("AppDatabaseRecycling");
            q.AddJob<AppDatabaseRecyclingJob>(opts => opts.WithIdentity(appDatabaseRecyclingJob));
            q.AddTrigger(opts => opts
                .ForJob(appDatabaseRecyclingJob)
                .WithIdentity("AppDatabaseRecyclingTrigger")
                .WithCronSchedule("0 0 0 * * ?"));

            // var postViewFlushJob = new JobKey("PostViewFlush");
            // q.AddJob<PostViewFlushJob>(opts => opts.WithIdentity(postViewFlushJob));
            // q.AddTrigger(opts => opts
            //     .ForJob(postViewFlushJob)
            //     .WithIdentity("PostViewFlushTrigger")
            //     .WithSimpleSchedule(o => o
            //         .WithIntervalInMinutes(1)
            //         .RepeatForever())
            // );

            var webFeedScraperJob = new JobKey("WebFeedScraper");
            q.AddJob<WebFeedScraperJob>(opts => opts.WithIdentity(webFeedScraperJob));
            q.AddTrigger(opts => opts
                .ForJob(webFeedScraperJob)
                .WithIdentity("WebFeedScraperTrigger")
                .WithSimpleSchedule(o => o.WithIntervalInHours(24).RepeatForever())
            );
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
