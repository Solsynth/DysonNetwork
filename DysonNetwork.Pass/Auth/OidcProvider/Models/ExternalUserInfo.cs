namespace DysonNetwork.Pass.Auth.OidcProvider.Models;

public class ExternalUserInfo
{
    public string Provider { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? Email { get; set; }
    public string? Name { get; set; }
}
