using DysonNetwork.Insight.Thought;
using Quartz;

namespace DysonNetwork.Insight.Startup;

public static class ScheduledJobsConfiguration
{
    public static IServiceCollection AddAppScheduledJobs(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            var tokenBillingJob = new JobKey("TokenBilling");
            q.AddJob<TokenBillingJob>(opts => opts.WithIdentity(tokenBillingJob));
            q.AddTrigger(opts => opts
                .ForJob(tokenBillingJob)
                .WithIdentity("TokenBillingTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(5)
                    .RepeatForever())
            );
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
