using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Drive.Index;
using DysonNetwork.Shared.Cache;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Drive.Startup;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddAppServices(IConfiguration configuration)
        {
            services.AddDbContext<AppDatabase>();
            services.AddHttpContextAccessor();

            services.AddHttpClient();

            // Register gRPC services
            services.AddGrpc(options =>
            {
                options.EnableDetailedErrors = true; // Will be adjusted in Program.cs
                options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16MB
                options.MaxSendMessageSize = 16 * 1024 * 1024; // 16MB
            });
            services.AddGrpcReflection();

            services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

                options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
            });

            return services;
        }

        public IServiceCollection AddAppAuthentication()
        {
            services.AddAuthorization();
            return services;
        }

        public IServiceCollection AddAppFlushHandlers()
        {
            services.AddSingleton<FlushBufferService>();

            return services;
        }

        public IServiceCollection AddAppBusinessServices(IConfiguration configuration)
        {
            services.Configure<Storage.Options.FileReanalysisOptions>(configuration.GetSection("FileReanalysis"));

            services.AddScoped<Storage.FileService>();
            services.AddScoped<Storage.FileReanalysisService>();
            services.AddScoped<Storage.PersistentTaskService>();
            services.AddScoped<FileIndexService>();
            services.AddScoped<Billing.UsageService>();
            services.AddScoped<Billing.QuotaService>();

            services.AddHostedService<BroadcastEventHandler>();
            services.AddHostedService<Storage.FileReanalysisBackgroundService>();

            return services;
        }
    }
}
