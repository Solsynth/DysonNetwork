using DysonNetwork.Pass.Credit;
using DysonNetwork.Pass.Handlers;
using DysonNetwork.Pass.Wallet;
using Quartz;

namespace DysonNetwork.Pass.Startup;

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

            var actionLogFlushJob = new JobKey("ActionLogFlush");
            q.AddJob<ActionLogFlushJob>(opts => opts.WithIdentity(actionLogFlushJob));
            q.AddTrigger(opts => opts
                .ForJob(actionLogFlushJob)
                .WithIdentity("ActionLogFlushTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(5)
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

            var subscriptionRenewalJob = new JobKey("SubscriptionRenewal");
            q.AddJob<SubscriptionRenewalJob>(opts => opts.WithIdentity(subscriptionRenewalJob));
            q.AddTrigger(opts => opts
                .ForJob(subscriptionRenewalJob)
                .WithIdentity("SubscriptionRenewalTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(30)
                    .RepeatForever())
            );

            var giftCleanupJob = new JobKey("GiftCleanup");
            q.AddJob<GiftCleanupJob>(opts => opts.WithIdentity(giftCleanupJob));
            q.AddTrigger(opts => opts
                .ForJob(giftCleanupJob)
                .WithIdentity("GiftCleanupTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInHours(1)
                    .RepeatForever())
            );

            var fundExpirationJob = new JobKey("FundExpiration");
            q.AddJob<FundExpirationJob>(opts => opts.WithIdentity(fundExpirationJob));
            q.AddTrigger(opts => opts
                .ForJob(fundExpirationJob)
                .WithIdentity("FundExpirationTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInHours(1)
                    .RepeatForever())
            );

            var lotteryDrawJob = new JobKey("LotteryDraw");
            q.AddJob<Lotteries.LotteryDrawJob>(opts => opts.WithIdentity(lotteryDrawJob));
            q.AddTrigger(opts => opts
                .ForJob(lotteryDrawJob)
                .WithIdentity("LotteryDrawTrigger")
                .WithCronSchedule("0 0 0 * * ?"));

            var socialCreditValidationJob = new JobKey("SocialCreditValidation");
            q.AddJob<SocialCreditValidationJob>(opts => opts.WithIdentity(socialCreditValidationJob));
            q.AddTrigger(opts => opts
                .ForJob(socialCreditValidationJob)
                .WithIdentity("SocialCreditValidationTrigger")
                .WithCronSchedule("0 0 0 * * ?"));
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
