using Quartz;
using DysonNetwork.Wallet.Payment;

namespace DysonNetwork.Wallet.Startup;

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

            q.AddJob<SubscriptionRenewalJob>(opts => opts.WithIdentity("SubscriptionRenewal"));
            q.AddTrigger(opts => opts
                .ForJob("SubscriptionRenewal")
                .WithIdentity("SubscriptionRenewalTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(30)
                    .RepeatForever())
            );

            q.AddJob<GiftCleanupJob>(opts => opts.WithIdentity("GiftCleanup"));
            q.AddTrigger(opts => opts
                .ForJob("GiftCleanup")
                .WithIdentity("GiftCleanupTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInHours(1)
                    .RepeatForever())
            );

            q.AddJob<FundExpirationJob>(opts => opts.WithIdentity("FundExpiration"));
            q.AddTrigger(opts => opts
                .ForJob("FundExpiration")
                .WithIdentity("FundExpirationTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInHours(1)
                    .RepeatForever())

            );

            q.AddJob<TransactionExpirationJob>(opts => opts.WithIdentity("TransactionExpiration"));
            q.AddTrigger(opts => opts
                .ForJob("TransactionExpiration")
                .WithIdentity("TransactionExpirationTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(15)
                    .RepeatForever())
            );

            q.AddJob<FundRaisingDeadlineJob>(opts => opts.WithIdentity("FundRaisingDeadline"));
            q.AddTrigger(opts => opts
                .ForJob("FundRaisingDeadline")
                .WithIdentity("FundRaisingDeadlineTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(15)
                    .RepeatForever())
            );

            q.AddJob<AppSettlementJob>(opts => opts.WithIdentity("AppSettlement"));
            q.AddTrigger(opts => opts
                .ForJob("AppSettlement")
                .WithIdentity("AppSettlementTrigger")
                .WithCronSchedule("0 0 0 * * ?"));


        });

        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
