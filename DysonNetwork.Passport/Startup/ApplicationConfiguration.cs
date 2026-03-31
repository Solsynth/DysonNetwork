using DysonNetwork.Passport.Account;
using DysonNetwork.Passport.Credit;
using DysonNetwork.Passport.Leveling;
using DysonNetwork.Passport.Nfc;
using DysonNetwork.Passport.Realm;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Networking;

namespace DysonNetwork.Passport.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.MapOpenApi();

        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseWebSockets();
        app.UseAuthentication();
        app.UseDyAuthModelProjection();
        app.UseAuthorization();

        app.MapControllers();

        return app;
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        app.MapGrpcService<AccountServiceGrpc>();
        app.MapGrpcService<MagicSpellServiceGrpc>();
        app.MapGrpcService<SocialCreditServiceGrpc>();
        app.MapGrpcService<ExperienceServiceGrpc>();
        app.MapGrpcService<RealmServiceGrpc>();
        app.MapGrpcService<NfcServiceGrpc>();
        app.MapGrpcReflectionService();

        return app;
    }
}
