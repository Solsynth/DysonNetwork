using NodaTime;

namespace DysonNetwork.Pass.Auth.OidcProvider.Models;

public class AuthorizationCodeInfo
{
    public Guid ClientId { get; set; }
    public Guid AccountId { get; set; }
    public string RedirectUri { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public string? Nonce { get; set; }
    public Instant CreatedAt { get; set; }
}
