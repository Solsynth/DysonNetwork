using DysonNetwork.Pass.Account;
using DysonNetwork.Pass.Account.Presences;
using DysonNetwork.Pass.Credit;
using DysonNetwork.Pass.Handlers;
using Quartz;

namespace DysonNetwork.Pass.Startup;

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

            q.AddJob<ActionLogFlushJob>(opts => opts.WithIdentity("ActionLogFlush"));
            q.AddTrigger(opts => opts
                .ForJob("ActionLogFlush")
                .WithIdentity("ActionLogFlushTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(5)
                    .RepeatForever())
            );

            q.AddJob<LastActiveFlushJob>(opts => opts.WithIdentity("LastActiveFlush"));
            q.AddTrigger(opts => opts
                .ForJob("LastActiveFlush")
                .WithIdentity("LastActiveFlushTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(5)
                    .RepeatForever())
            );

            q.AddJob<SocialCreditValidationJob>(opts => opts.WithIdentity("SocialCreditValidation"));
            q.AddTrigger(opts => opts
                .ForJob("SocialCreditValidation")
                .WithIdentity("SocialCreditValidationTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(5)
                    .RepeatForever()));

            // Presence update jobs for different user stages
            q.AddJob<PresenceUpdateJob>(opts => opts.WithIdentity("ActivePresenceUpdate"));
            q.AddTrigger(opts => opts
                .ForJob("ActivePresenceUpdate")
                .WithIdentity("ActivePresenceUpdateTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(1)
                    .RepeatForever())
                .UsingJobData("stage", nameof(PresenceUpdateStage.Active))
            );

            q.AddJob<PresenceUpdateJob>(opts => opts.WithIdentity("MaybePresenceUpdate"));
            q.AddTrigger(opts => opts
                .ForJob("MaybePresenceUpdate")
                .WithIdentity("MaybePresenceUpdateTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(5)
                    .RepeatForever())
                .UsingJobData("stage", nameof(PresenceUpdateStage.Maybe))
            );

            q.AddJob<PresenceUpdateJob>(opts => opts.WithIdentity("ColdPresenceUpdate"));
            q.AddTrigger(opts => opts
                .ForJob("ColdPresenceUpdate")
                .WithIdentity("ColdPresenceUpdateTrigger")
                .WithSimpleSchedule(o => o
                    .WithIntervalInMinutes(10)
                    .RepeatForever())
                .UsingJobData("stage", nameof(PresenceUpdateStage.Cold))
            );
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
