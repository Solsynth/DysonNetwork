namespace DysonNetwork.Pass.Startup;

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
        });

        return builder;
    }
}
