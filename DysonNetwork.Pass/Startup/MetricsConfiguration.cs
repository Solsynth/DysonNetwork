using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Prometheus;
using Prometheus.SystemMetrics;

namespace DysonNetwork.Pass.Startup;

public static class MetricsConfiguration
{
    public static IServiceCollection AddAppMetrics(this IServiceCollection services)
    {
        // Prometheus
        services.UseHttpClientMetrics();
        services.AddHealthChecks();
        services.AddSystemMetrics();
        services.AddPrometheusEntityFrameworkMetrics();
        services.AddPrometheusAspNetCoreMetrics();
        services.AddPrometheusHttpClientMetrics();

        // OpenTelemetry
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter();
            });

        return services;
    }
}
