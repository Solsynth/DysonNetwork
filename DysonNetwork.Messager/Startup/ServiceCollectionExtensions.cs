using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Messager.Chat;
using DysonNetwork.Messager.Chat.Realtime;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Messager.Startup;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddAppServices()
        {
            services.AddDbContext<AppDatabase>();
            services.AddHttpContextAccessor();

            services.AddHttpClient();

            services
                .AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.NumberHandling =
                        JsonNumberHandling.AllowNamedFloatingPointLiterals;
                    options.JsonSerializerOptions.PropertyNamingPolicy =
                        JsonNamingPolicy.SnakeCaseLower;

                    options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
                });

            services.AddGrpc(options =>
            {
                options.EnableDetailedErrors = true;
            });
            services.AddGrpcReflection();

            return services;
        }

        public IServiceCollection AddAppAuthentication()
        {
            services.AddAuthorization();
            return services;
        }

        public IServiceCollection AddAppBusinessServices(IConfiguration configuration
        )
        {
            services.AddScoped<ChatRoomService>();
            services.AddScoped<ChatService>();
            services.AddScoped<IRealtimeService, LiveKitRealtimeService>();

            services.AddHostedService<BroadcastEventHandler>();

            return services;
        }
    }
}
