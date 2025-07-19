using System.Text.Json.Serialization;

namespace DysonNetwork.Pass.Auth.OidcProvider.Responses;

public class AuthorizationResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }


    [JsonPropertyName("session_state")]
    public string? SessionState { get; set; }


    [JsonPropertyName("iss")]
    public string? Issuer { get; set; }
}
