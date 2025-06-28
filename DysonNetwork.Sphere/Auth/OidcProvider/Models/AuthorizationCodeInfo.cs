using System;
using System.Collections.Generic;
using NodaTime;

namespace DysonNetwork.Sphere.Auth.OidcProvider.Models;

public class AuthorizationCodeInfo
{
    public Guid ClientId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public string? Nonce { get; set; }
    public Instant Expiration { get; set; }
    public Instant CreatedAt { get; set; }
}
