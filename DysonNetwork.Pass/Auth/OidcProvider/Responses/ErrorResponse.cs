using System.Text.Json.Serialization;

namespace DysonNetwork.Pass.Auth.OidcProvider.Responses;

public class ErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = null!;

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }


    [JsonPropertyName("error_uri")]
    public string? ErrorUri { get; set; }


    [JsonPropertyName("state")]
    public string? State { get; set; }
}
