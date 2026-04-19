using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.Reader;
using DysonNetwork.Insight.SnChan;
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

            q.AddJob<FreeQuotaResetJob>(opts => opts.WithIdentity("FreeQuotaReset").StoreDurably());
            q.AddTrigger(opts => opts
                .ForJob("FreeQuotaReset")
                .WithIdentity("FreeQuotaResetTrigger")
                .WithCronSchedule("0 0 0 * * ?")
            );

            q.AddJob<WebFeedScraperJob>(opts => opts.WithIdentity("WebFeedScraper").StoreDurably());
            q.AddTrigger(opts => opts
                .ForJob("WebFeedScraper")
                .WithIdentity("WebFeedScraperTrigger")
                .WithCronSchedule("0 0 0 * * ?")
            );

            q.AddJob<WebFeedVerificationJob>(opts => opts.WithIdentity("WebFeedVerification").StoreDurably());
            q.AddTrigger(opts => opts
                .ForJob("WebFeedVerification")
                .WithIdentity("WebFeedVerificationTrigger")
                .WithCronSchedule("0 0 4 * * ?")
            );

            q.AddJob<ScheduledTaskJob>(opts => opts.WithIdentity("ScheduledTask").StoreDurably());
            q.AddTrigger(opts => opts
                .ForJob("ScheduledTask")
                .WithIdentity("ScheduledTaskTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(1)
                    .RepeatForever())
            );

            q.AddJob<SnChanReplyMonitorJob>(opts => opts.WithIdentity("SnChanReplyMonitor").StoreDurably());
            q.AddTrigger(opts => opts
                .ForJob("SnChanReplyMonitor")
                .WithIdentity("SnChanReplyMonitorTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(10)
                    .RepeatForever())
            );
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
