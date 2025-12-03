using DysonNetwork.Ring.Services;
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
            
            q.AddJob<PushSubFlushJob>(opts => opts.WithIdentity("PushSubFlush"));
            q.AddTrigger(opts => opts
                .ForJob("PushSubFlush")
                .WithIdentity("PushSubFlushTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(5)
                    .RepeatForever())
            ); 
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
