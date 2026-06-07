using DysonNetwork.Shared.Models;

namespace DysonNetwork.Padlock.Auth.OidcProvider.Responses;

public class ClientInfoResponse
{
    public string ClientId { get; set; } = null!;
    public SnCloudFileReferenceObject? Picture { get; set; }
    public SnCloudFileReferenceObject? Background { get; set; }
    public string? ClientName { get; set; }
    public string? HomeUri { get; set; }
    public string? PolicyUri { get; set; }
    public string? TermsOfServiceUri { get; set; }
    public string? ResponseTypes { get; set; }
    public string[]? Scopes { get; set; }
    public string? State { get; set; }
    public string? Nonce { get; set; }
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
}
