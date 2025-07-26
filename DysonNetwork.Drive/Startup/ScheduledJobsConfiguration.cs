using DysonNetwork.Drive.Storage;
using Quartz;

namespace DysonNetwork.Drive.Startup;

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
            
            var cloudFileUnusedRecyclingJob = new JobKey("CloudFileUnusedRecycling");
            q.AddJob<CloudFileUnusedRecyclingJob>(opts => opts.WithIdentity(cloudFileUnusedRecyclingJob));
            q.AddTrigger(opts => opts
                .ForJob(cloudFileUnusedRecyclingJob)
                .WithIdentity("CloudFileUnusedRecyclingTrigger")
                .WithCronSchedule("0 0 0 * * ?"));
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
