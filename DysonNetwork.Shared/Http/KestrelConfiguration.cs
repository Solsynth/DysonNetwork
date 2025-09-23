using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace DysonNetwork.Shared.Http;

public static class KestrelConfiguration
{
    public static WebApplicationBuilder ConfigureAppKestrel(
        this WebApplicationBuilder builder,
        IConfiguration configuration,
        long maxRequestBodySize = 50 * 1024 * 1024,
        bool enableGrpc = true
    )
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = maxRequestBodySize;

            if (enableGrpc)
            {
                // gRPC
                var grpcPort = int.Parse(configuration.GetValue<string>("GRPC_PORT", "5001"));
                options.ListenAnyIP(grpcPort, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;

                    var selfSignedCert = _CreateSelfSignedCertificate();
                    listenOptions.UseHttps(selfSignedCert);
                });
            }


            var httpPorts = configuration.GetValue<string>("HTTP_PORTS", "6000")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => int.Parse(p.Trim()))
                .ToArray();

            // Regular HTTP
            foreach (var httpPort in httpPorts)
                options.ListenAnyIP(httpPort,
                    listenOptions => { listenOptions.Protocols = HttpProtocols.Http1AndHttp2; });
        });

        return builder;
    }

    static X509Certificate2 _CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var certRequest = new CertificateRequest(
            "CN=dyson.network", // Common Name for the certificate
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add extensions (e.g., for server authentication)
        certRequest.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
                false));

        // Set validity period (e.g., 1 year)
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = notBefore.AddYears(1);

        var certificate = certRequest.CreateSelfSigned(notBefore, notAfter);

        // Export to PKCS#12 and load using X509CertificateLoader
        var pfxBytes = certificate.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, password: null);
    }
}
