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
            
            var configuredUrl = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            if (!string.IsNullOrEmpty(configuredUrl)) return;

            var certPath = configuration["Service:ClientCert"]!;
            var keyPath = configuration["Service:ClientKey"]!;

            // Load PEM cert and key manually
            var certificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            // Now pass the full cert
            options.ListenAnyIP(5001, listenOptions => { listenOptions.UseHttps(certificate); });

            // Optional: HTTP fallback
            options.ListenAnyIP(8080);
        });

        return builder;
    }
}