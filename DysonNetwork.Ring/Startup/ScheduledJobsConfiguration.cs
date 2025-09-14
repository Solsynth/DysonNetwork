using DysonNetwork.Ring.Notification;
using Quartz;

namespace DysonNetwork.Ring.Startup;

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

            var notificationFlushJob = new JobKey("NotificationFlush");
            q.AddJob<NotificationFlushJob>(opts => opts.WithIdentity(notificationFlushJob));
            q.AddTrigger(opts => opts
                .ForJob(notificationFlushJob)
                .WithIdentity("NotificationFlushTrigger")
                .WithSimpleSchedule(a => a.WithIntervalInSeconds(60).RepeatForever()));
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
