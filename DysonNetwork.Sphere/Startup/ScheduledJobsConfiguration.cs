using DysonNetwork.Sphere.Storage;
using DysonNetwork.Sphere.Storage.Handlers;
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

            var cloudFilesRecyclingJob = new JobKey("CloudFilesUnusedRecycling");
            q.AddJob<CloudFileUnusedRecyclingJob>(opts => opts.WithIdentity(cloudFilesRecyclingJob));
            q.AddTrigger(opts => opts
                .ForJob(cloudFilesRecyclingJob)
                .WithIdentity("CloudFilesUnusedRecyclingTrigger")
                .WithSimpleSchedule(o => o.WithIntervalInHours(1).RepeatForever())
            );

            var actionLogFlushJob = new JobKey("ActionLogFlush");
            q.AddJob<ActionLogFlushJob>(opts => opts.WithIdentity(actionLogFlushJob));
            q.AddTrigger(opts => opts
                .ForJob(actionLogFlushJob)
                .WithIdentity("ActionLogFlushTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(5)
                    .RepeatForever())
            );

            var readReceiptFlushJob = new JobKey("ReadReceiptFlush");
            q.AddJob<ReadReceiptFlushJob>(opts => opts.WithIdentity(readReceiptFlushJob));
            q.AddTrigger(opts => opts
                .ForJob(readReceiptFlushJob)
                .WithIdentity("ReadReceiptFlushTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInSeconds(60)
                    .RepeatForever())
            );

            var lastActiveFlushJob = new JobKey("LastActiveFlush");
            q.AddJob<LastActiveFlushJob>(opts => opts.WithIdentity(lastActiveFlushJob));
            q.AddTrigger(opts => opts
                .ForJob(lastActiveFlushJob)
                .WithIdentity("LastActiveFlushTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(5)
                    .RepeatForever())
            );

            var postViewFlushJob = new JobKey("PostViewFlush");
            q.AddJob<PostViewFlushJob>(opts => opts.WithIdentity(postViewFlushJob));
            q.AddTrigger(opts => opts
                .ForJob(postViewFlushJob)
                .WithIdentity("PostViewFlushTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(1)
                    .RepeatForever())
            );
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
