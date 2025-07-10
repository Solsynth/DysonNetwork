namespace DysonNetwork.Pass.Auth.OpenId;

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

    public Dictionary<string, object> ToMetadata()
    {
        var metadata = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(UserId))
            metadata["user_id"] = UserId;

        if (!string.IsNullOrWhiteSpace(Email))
            metadata["email"] = Email;

        metadata["email_verified"] = EmailVerified;

        if (!string.IsNullOrWhiteSpace(FirstName))
            metadata["first_name"] = FirstName;

        if (!string.IsNullOrWhiteSpace(LastName))
            metadata["last_name"] = LastName;

        if (!string.IsNullOrWhiteSpace(DisplayName))
            metadata["display_name"] = DisplayName;

        if (!string.IsNullOrWhiteSpace(PreferredUsername))
            metadata["preferred_username"] = PreferredUsername;

        if (!string.IsNullOrWhiteSpace(ProfilePictureUrl))
            metadata["profile_picture_url"] = ProfilePictureUrl;

        return metadata;
    }
}