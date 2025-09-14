using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DysonNetwork.Shared.Http;

public static class KestrelConfiguration
{
    public static WebApplicationBuilder ConfigureAppKestrel(
        this WebApplicationBuilder builder,
        IConfiguration configuration,
        long maxRequestBodySize = 50 * 1024 * 1024
    )
    {
        builder.Host.UseContentRoot(Directory.GetCurrentDirectory());
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = maxRequestBodySize;
        });

        return builder;
    }
}