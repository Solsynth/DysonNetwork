using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Zone.Publication;
using DysonNetwork.Zone.SEO;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Zone.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.AddDbContext<AppDatabase>();
        services.AddHttpContextAccessor();
        services.AddSingleton<MarkdownConverter>();

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
        services.AddRazorPages();

        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true;
        });
        services.AddGrpcReflection();

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new[] { new CultureInfo("en-US"), new CultureInfo("zh-Hans") };

            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });

        services.AddEventBus()
            .AddListener<AccountDeletedEvent>(
                AccountDeletedEvent.Type,
                async (evt, ctx) =>
                {
                    var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                    var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();

                    logger.LogWarning("Account deleted: {AccountId}", evt.AccountId);

                    // TODO clean up data
                },
                opts =>
                {
                    opts.UseJetStream = true;
                    opts.StreamName = "account_events";
                    opts.ConsumerName = "zone_account_deleted_handler";
                }
            );

        return services;
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddAppFlushHandlers(this IServiceCollection services)
    {
        services.AddSingleton<FlushBufferService>();

        return services;
    }

    public static IServiceCollection AddAppBusinessServices(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<GeoOptions>(configuration.GetSection("GeoIP"));
        services.AddScoped<GeoService>();

        services.AddScoped<PublicationSiteService>();
        services.AddScoped<PublicationSiteManager>();
        services.AddScoped<TemplateRouteResolver>();
        services.AddScoped<TemplateContextBuilder>();
        services.AddScoped<TemplateSiteRenderer>();
        services.AddScoped<SiteRssService>();
        services.AddScoped<SiteSitemapService>();
        services.AddMemoryCache();

        return services;
    }
}
