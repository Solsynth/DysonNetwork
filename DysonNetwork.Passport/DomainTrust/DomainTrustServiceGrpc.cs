using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Passport.DomainTrust;

public class DomainTrustServiceGrpc(DomainTrustService service)
    : DyDomainBlockService.DyDomainBlockServiceBase
{
    public override async Task<DyDomainValidationResult> ValidateUrl(
        DyValidateUrlRequest request,
        ServerCallContext context
    )
    {
        var result = await service.ValidateUrlAsync(request.Url);

        return new DyDomainValidationResult
        {
            IsAllowed = result.IsAllowed,
            IsVerified = result.IsVerified,
            BlockReason = result.BlockReason ?? string.Empty,
            MatchedSource = result.MatchedSource ?? string.Empty
        };
    }

    public override async Task<DyDomainBlockCheckResult> IsDomainBlocked(
        DyDomainBlockCheckRequest request,
        ServerCallContext context
    )
    {
        int? port = request.Port > 0 ? request.Port : null;

        var rule = await service.FindMatchingRuleAsync(
            request.Host,
            string.IsNullOrEmpty(request.Protocol) ? null : request.Protocol,
            port
        );

        return new DyDomainBlockCheckResult
        {
            IsBlocked = rule != null && rule.TrustLevel == DomainTrustLevel.Blocked,
            Reason = rule?.Reason ?? string.Empty
        };
    }
}
