using System.Text.Json;
using System.Threading.RateLimiting;
using DysonNetwork.Shared.Cache;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using StackExchange.Redis;
using DysonNetwork.Shared.Proto;
using tusdotnet.Stores;

namespace DysonNetwork.Drive.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDatabase>(); // Assuming you'll have an AppDatabase
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connection = configuration.GetConnectionString("FastRetrieve")!;
            return ConnectionMultiplexer.Connect(connection);
        });
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddHttpContextAccessor();
        services.AddSingleton<ICacheService, CacheServiceRedis>(); // Uncomment if you have CacheServiceRedis

        services.AddHttpClient();

        // Register gRPC services
        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true; // Will be adjusted in Program.cs
            options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16MB
            options.MaxSendMessageSize = 16 * 1024 * 1024; // 16MB
        });

        // Register gRPC reflection for service discovery
        services.AddGrpc();

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

            options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        });

        return services;
    }

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o => o.AddFixedWindowLimiter(policyName: "fixed", opts =>
        {
            opts.Window = TimeSpan.FromMinutes(1);
            opts.PermitLimit = 120;
            opts.QueueLimit = 2;
            opts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        }));

        return services;
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddCors();
        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddAppFlushHandlers(this IServiceCollection services)
    {
        services.AddSingleton<FlushBufferService>();

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
                Title = "Dyson Drive",
                Description =
                    "The file service of the Dyson Network. Mainly handling file storage and sharing. Also provide image processing and media analysis. Powered the Solar Network Drive as well.",
                TermsOfService = new Uri("https://solsynth.dev/terms"), // Update with actual terms
                License = new OpenApiLicense
                {
                    Name = "APGLv3", // Update with actual license
                    Url = new Uri("https://www.gnu.org/licenses/agpl-3.0.html")
                }
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "Please enter a valid token",
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                BearerFormat = "JWT",
                Scheme = "Bearer"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    []
                }
            });
        });

        return services;
    }
    
    public static IServiceCollection AddAppFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var tusStorePath = configuration.GetSection("Tus").GetValue<string>("StorePath")!;
        Directory.CreateDirectory(tusStorePath);
        var tusDiskStore = new TusDiskStore(tusStorePath);

        services.AddSingleton(tusDiskStore);

        return services;
    }

    public static IServiceCollection AddAppBusinessServices(this IServiceCollection services)
    {
        services.AddScoped<Storage.FileService>();
        services.AddScoped<Storage.FileReferenceService>();
        services.AddScoped<Billing.UsageService>();
        services.AddScoped<Billing.QuotaService>();
        
        return services;
    }
}