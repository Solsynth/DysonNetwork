using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace DysonNetwork.Shared.Registry;

public static class GatewayReverseProxy
{
    /// <summary>
    /// Provides reverse proxy for DysonNetwork.Gateway.
    /// Give the ability to the contained frontend to access other services via the gateway.
    /// </summary>
    /// <param name="app">The asp.net core application</param>
    /// <returns>The modified application</returns>
    public static WebApplication MapGatewayProxy(this WebApplication app)
    {
        var httpClient = new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            EnableMultipleHttp2Connections = true,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        });

        var transformer = new GatewayReverseProxyTransformer();
        var requestConfig = new ForwarderRequestConfig();

        app.Map("/cgi/{**catch-all}", async (HttpContext context, IHttpForwarder forwarder) =>
        {
            var registry = context.RequestServices.GetRequiredService<ServiceRegistry>();
            var gatewayUrl = await registry.GetServiceUrl("DysonNetwork.Gateway");
            if (gatewayUrl is null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Gateway not found");
                return;
            }

            var error = await forwarder.SendAsync(
                context,
                gatewayUrl,
                httpClient,
                requestConfig,
                transformer
            );
            if (error != ForwarderError.None)
            {
                var errorFeature = context.GetForwarderErrorFeature();
                var exception = errorFeature?.Exception;
                context.Response.StatusCode = 502;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync($"Gateway remote error: {exception?.Message}");
            }
        });

        return app;
    }
}

public class GatewayReverseProxyTransformer : HttpTransformer
{
    private const string Value = "/cgi";
    
    public override ValueTask TransformRequestAsync(
        HttpContext httpContext,
        HttpRequestMessage proxyRequest,
        string destinationPrefix,
        CancellationToken cancellationToken
    )
    {
        httpContext.Request.Path = httpContext.Request.Path.StartsWithSegments(Value, out var remaining)
            ? remaining
            : httpContext.Request.Path;
        
        return Default.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
    }
}