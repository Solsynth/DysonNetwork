using System.Text.Json.Serialization;

namespace DysonNetwork.Pass.Auth.OidcProvider.Responses;

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; } = null!;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }


    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }
    
    [JsonPropertyName("onboarding_token")]
    public string? OnboardingToken { get; set; }
}
