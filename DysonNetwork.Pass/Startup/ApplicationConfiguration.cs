using System.Net;
using DysonNetwork.Pass.Account;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Credit;
using DysonNetwork.Pass.Leveling;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Prometheus;

namespace DysonNetwork.Pass.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration, string contentRoot)
    {
        app.MapMetrics();
        app.MapOpenApi();

        app.UseSwagger();
        app.UseSwaggerUI();

        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseCors(opts =>
            opts.SetIsOriginAllowed(_ => true)
                .WithExposedHeaders("*")
                .WithHeaders("*")
                .AllowCredentials()
                .AllowAnyHeader()
                .AllowAnyMethod()
        );

        app.UseWebSockets();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<PermissionMiddleware>();

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(contentRoot, "wwwroot", "dist"))
        });

        app.MapControllers().RequireRateLimiting("fixed");

        return app;
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        app.MapGrpcService<AccountServiceGrpc>();
        app.MapGrpcService<AuthServiceGrpc>();
        app.MapGrpcService<ActionLogServiceGrpc>();
        app.MapGrpcService<PermissionServiceGrpc>();
        app.MapGrpcService<SocialCreditServiceGrpc>();
        app.MapGrpcService<ExperienceServiceGrpc>();
        app.MapGrpcService<BotAccountReceiverGrpc>();
        app.MapGrpcService<WalletServiceGrpc>();
        app.MapGrpcService<PaymentServiceGrpc>();

        return app;
    }
}