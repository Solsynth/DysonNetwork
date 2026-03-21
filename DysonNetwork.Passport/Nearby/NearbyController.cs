using System.ComponentModel.DataAnnotations;
using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;

namespace DysonNetwork.Passport.Nearby;

[ApiController]
[Route("/api/nearby")]
public class NearbyController(
    NearbyService nearby
) : ControllerBase
{
    public class PresenceTokensRequest
    {
        [Required]
        [MaxLength(256)]
        public string DeviceId { get; set; } = string.Empty;

        public bool Discoverable { get; set; } = true;
        public bool FriendOnly { get; set; } = true;
        public int Capabilities { get; set; }
        [Range(1, NearbyService.MaxPrefetchSlots)]
        public int PrefetchSlots { get; set; } = NearbyService.DefaultPrefetchSlots;
    }

    public class PresenceTokenDto
    {
        public long Slot { get; set; }
        public string Token { get; set; } = string.Empty;
        public Instant ValidFrom { get; set; }
        public Instant ValidTo { get; set; }
    }

    public class PresenceTokensResponse
    {
        public string ServiceUuid { get; set; } = NearbyService.DefaultServiceUuid;
        public int SlotDurationSec { get; set; }
        public List<PresenceTokenDto> Tokens { get; set; } = [];
    }

    public class ResolveObservationRequest
    {
        [Required]
        [StringLength(NearbyService.TokenBytes * 2, MinimumLength = NearbyService.TokenBytes * 2)]
        public string Token { get; set; } = string.Empty;

        public long Slot { get; set; }
        public int AvgRssi { get; set; }
        public int SeenCount { get; set; }
        public long DurationMs { get; set; }
        public Instant? FirstSeenAt { get; set; }
        public Instant? LastSeenAt { get; set; }
    }

    public class ResolveRequest
    {
        [Required]
        [MinLength(1)]
        public List<ResolveObservationRequest> Observations { get; set; } = [];
    }

    public class ResolvePeerResponse
    {
        public Guid UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public SnCloudFileReferenceObject? Avatar { get; set; }
        public bool IsFriend { get; set; }
        public bool CanInvite { get; set; }
        public string Visibility { get; set; } = "friend_only";
        public Instant LastSeenAt { get; set; }
    }

    public class ResolveResponse
    {
        public List<ResolvePeerResponse> Peers { get; set; } = [];
    }

    [HttpPost("presence-tokens")]
    [Authorize]
    public async Task<ActionResult<PresenceTokensResponse>> IssuePresenceTokens(
        [FromBody] PresenceTokensRequest request,
        CancellationToken cancellationToken
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var tokens = await nearby.IssuePresenceTokensAsync(
            currentUser.Id,
            request.DeviceId,
            request.Discoverable,
            request.FriendOnly,
            request.Capabilities,
            request.PrefetchSlots,
            cancellationToken
        );

        return Ok(new PresenceTokensResponse
        {
            ServiceUuid = nearby.GetServiceUuid(),
            SlotDurationSec = nearby.GetSlotDurationSec(),
            Tokens = tokens.Select(t => new PresenceTokenDto
            {
                Slot = t.Slot,
                Token = t.Token,
                ValidFrom = t.ValidFrom,
                ValidTo = t.ValidTo
            }).ToList()
        });
    }

    [HttpPost("resolve")]
    [Authorize]
    public async Task<ActionResult<ResolveResponse>> Resolve(
        [FromBody] ResolveRequest request,
        CancellationToken cancellationToken
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var peers = await nearby.ResolveAsync(
            currentUser.Id,
            request.Observations.Select(o => new NearbyObservation
            {
                Token = o.Token,
                Slot = o.Slot,
                AvgRssi = o.AvgRssi,
                SeenCount = o.SeenCount,
                DurationMs = o.DurationMs,
                FirstSeenAt = o.FirstSeenAt,
                LastSeenAt = o.LastSeenAt
            }).ToList(),
            cancellationToken
        );

        return Ok(new ResolveResponse
        {
            Peers = peers.Select(p => new ResolvePeerResponse
            {
                UserId = p.UserId,
                DisplayName = p.DisplayName,
                Avatar = p.Avatar,
                IsFriend = p.IsFriend,
                CanInvite = p.CanInvite,
                Visibility = p.Visibility,
                LastSeenAt = p.LastSeenAt
            }).ToList()
        });
    }
}
