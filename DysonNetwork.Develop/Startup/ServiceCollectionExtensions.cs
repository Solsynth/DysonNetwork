using System.Globalization;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Develop.Identity;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Cache;
using StackExchange.Redis;

namespace DysonNetwork.Develop.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLocalization();

        services.AddDbContext<AppDatabase>();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddHttpContextAccessor();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connection = configuration.GetConnectionString("FastRetrieve")!;
            return ConnectionMultiplexer.Connect(connection);
        });
        services.AddSingleton<ICacheService, CacheServiceRedis>();

        services.AddHttpClient();

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
            
            options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        });

        services.AddGrpc(options => { options.EnableDetailedErrors = true; });

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

        services.AddScoped<DeveloperService>();
        services.AddScoped<CustomAppService>();
        services.AddScoped<DevProjectService>();
        services.AddScoped<BotAccountService>();

        return services;
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddCors();
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddAppSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Version = "v1",
                Title = "Develop API",
            });
        });
        services.AddOpenApi();
        return services;
    }
}
