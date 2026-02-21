using System.Globalization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Fitness.ExerciseLibrary;
using DysonNetwork.Fitness.Goals;
using DysonNetwork.Fitness.Metrics;
using DysonNetwork.Fitness.Workouts;

namespace DysonNetwork.Fitness.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLocalization();

        services.AddDbContext<AppDatabase>();
        services.AddHttpContextAccessor();

        services.AddHttpClient();

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
            
            options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        });

        services.AddGrpc(options => { options.EnableDetailedErrors = true; });
        services.AddGrpcReflection();

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new[]
            {
                new CultureInfo("en-US"),
                new CultureInfo("zh-Hans"),
            };

            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });

        // Add distributed cache (Redis)
        services.AddDistributedMemoryCache();
        
        // Add fitness services
        services.AddScoped<WorkoutService>();
        services.AddScoped<MetricService>();
        services.AddScoped<GoalService>();
        services.AddScoped<ExerciseLibraryService>();

        return services;
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthorization();
        return services;
    }
}
