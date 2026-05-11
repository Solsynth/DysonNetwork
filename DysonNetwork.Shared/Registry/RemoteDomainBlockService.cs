using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class DomainValidationResult
{
    public bool IsAllowed { get; set; }
    public bool IsVerified { get; set; }
    public string? BlockReason { get; set; }
    public string? MatchedSource { get; set; }
}

public class RemoteDomainBlockService(DyDomainBlockService.DyDomainBlockServiceClient domainBlock)
{
    public async Task<DomainValidationResult> ValidateUrlAsync(string url)
    {
        try
        {
            var request = new DyValidateUrlRequest { Url = url };
            var response = await domainBlock.ValidateUrlAsync(request);

            return new DomainValidationResult
            {
                IsAllowed = response.IsAllowed,
                IsVerified = response.IsVerified,
                BlockReason = string.IsNullOrEmpty(response.BlockReason) ? null : response.BlockReason,
                MatchedSource = string.IsNullOrEmpty(response.MatchedSource) ? null : response.MatchedSource
            };
        }
        catch
        {
            return new DomainValidationResult
            {
                IsAllowed = false,
                IsVerified = false,
                BlockReason = "Domain validation service unavailable",
                MatchedSource = "grpc_error"
            };
        }
    }

    public async Task<bool> IsDomainBlockedAsync(string host, string? protocol = null, int? port = null)
    {
        try
        {
            var request = new DyDomainBlockCheckRequest
            {
                Host = host,
                Protocol = protocol ?? string.Empty,
                Port = port ?? 0
            };
            var response = await domainBlock.IsDomainBlockedAsync(request);
            return response.IsBlocked;
        }
        catch
        {
            return true;
        }
    }
}
