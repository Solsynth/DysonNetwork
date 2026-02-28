using Quartz;
using DysonNetwork.Messager.Chat.Voice;

namespace DysonNetwork.Messager.Startup;

public static class ScheduledJobsConfiguration
{
    public static IServiceCollection AddAppScheduledJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var voiceConfig = configuration
            .GetSection("VoiceMessages")
            .Get<ChatVoiceConfiguration>() ?? new ChatVoiceConfiguration();

        services.AddQuartz(q =>
        {
            q.AddJob<AppDatabaseRecyclingJob>(opts => opts.WithIdentity("AppDatabaseRecycling"));
            q.AddTrigger(opts => opts
                .ForJob("AppDatabaseRecycling")
                .WithIdentity("AppDatabaseRecyclingTrigger")
                .WithCronSchedule("0 0 0 * * ?"));

            q.AddJob<ChatVoiceCleanupJob>(opts => opts.WithIdentity("ChatVoiceCleanup"));
            q.AddTrigger(opts => opts
                .ForJob("ChatVoiceCleanup")
                .WithIdentity("ChatVoiceCleanupTrigger")
                .WithCronSchedule(voiceConfig.CleanupCron));
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
