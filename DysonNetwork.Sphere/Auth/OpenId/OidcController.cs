using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Sphere.Auth.OpenId;

[ApiController]
[Route("/auth/login")]
public class OidcController(
    IServiceProvider serviceProvider,
    AppDatabase db,
    Account.AccountService accountService,
    AuthService authService
)
    : ControllerBase
{
    [HttpGet("{provider}")]
    public ActionResult SignIn([FromRoute] string provider, [FromQuery] string? returnUrl = "/")
    {
        try
        {
            // Get the appropriate provider service
            var oidcService = GetOidcService(provider);

            // Generate state (containing return URL) and nonce
            var state = returnUrl;
            var nonce = Guid.NewGuid().ToString();

            // Get the authorization URL and redirect the user
            var authUrl = oidcService.GetAuthorizationUrl(state, nonce);
            return Redirect(authUrl);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error initiating OpenID Connect flow: {ex.Message}");
        }
    }

    private OidcService GetOidcService(string provider)
    {
        return provider.ToLower() switch
        {
            "apple" => serviceProvider.GetRequiredService<AppleOidcService>(),
            "google" => serviceProvider.GetRequiredService<GoogleOidcService>(),
            // Add more providers as needed
            _ => throw new ArgumentException($"Unsupported provider: {provider}")
        };
    }
}