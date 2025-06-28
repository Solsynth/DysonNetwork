using System;

namespace DysonNetwork.Sphere.Auth.OidcProvider.Options;

public class OidcProviderOptions
{
    public string IssuerUri { get; set; } = "https://your-issuer-uri.com";
    public string SigningKey { get; set; } = "replace-with-a-secure-random-key";
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public bool RequireHttpsMetadata { get; set; } = true;
}
