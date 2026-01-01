using DysonNetwork.Insight.Reader;
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
