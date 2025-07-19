using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;

namespace DysonNetwork.Shared.Http;

public static class KestrelConfiguration
{
    public static WebApplicationBuilder ConfigureAppKestrel(this WebApplicationBuilder builder)
    {
        builder.Host.UseContentRoot(Directory.GetCurrentDirectory());
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;
            options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
            options.ConfigureEndpointDefaults(endpoints =>
            {
                endpoints.Protocols = HttpProtocols.Http1AndHttp2;
            });
        });

        return builder;
    }
}