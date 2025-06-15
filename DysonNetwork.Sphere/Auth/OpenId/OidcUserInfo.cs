namespace DysonNetwork.Sphere.Auth.OpenId;

/// <summary>
/// Represents the user information from an OIDC provider
/// </summary>
public class OidcUserInfo
{
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PreferredUsername { get; set; } = "";
    public string? ProfilePictureUrl { get; set; }
    public string Provider { get; set; } = "";
    public string? RefreshToken { get; set; }
    public string? AccessToken { get; set; }
}