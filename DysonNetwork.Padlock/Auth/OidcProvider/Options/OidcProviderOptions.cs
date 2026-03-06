namespace DysonNetwork.Padlock.Auth.OidcProvider.Options;

public class OidcProviderOptions
{
    public TimeSpan AuthorizationCodeExpiration { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan IdTokenExpiration { get; set; } = TimeSpan.FromHours(1);
}
