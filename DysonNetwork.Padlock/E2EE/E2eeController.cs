using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnAuthSession = DysonNetwork.Shared.Models.SnAuthSession;

namespace DysonNetwork.Padlock.E2EE;

[ApiController]
[Route("/api/e2ee")]
[Authorize]
public class E2eeController(IGroupE2eeModule e2eeModule) : ControllerBase
{
    private const string AbilityHeader = "X-Client-Ability";
    private const string MlsAbilityToken = "chat.mls.v2";
    private static string? ResolveDeviceId(SnAuthSession session) => session.Client?.DeviceId;
    private bool HasAbility(string token)
    {
        if (!Request.Headers.TryGetValue(AbilityHeader, out var rawValue)) return false;
        foreach (var raw in rawValue)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var tokens = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Any(x => string.Equals(x, token, StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private ActionResult? EnsureMlsAbility()
    {
        if (HasAbility(MlsAbilityToken)) return null;
        return StatusCode(409, new
        {
            code = "e2ee.mls_ability_required",
            error = $"Missing required ability header: {AbilityHeader}: {MlsAbilityToken}"
        });
    }


    public class PublishMlsKeyPackageBody
    {
        [Required] public byte[] KeyPackage { get; set; } = [];
        [MaxLength(128)] public string Ciphersuite { get; set; } = "MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519";
        [Required][MaxLength(1024)] public string DeviceId { get; set; }
        [MaxLength(1024)] public string? DeviceLabel { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class BootstrapMlsGroupBody
    {
        [Required] public long Epoch { get; set; }
        public long StateVersion { get; set; } = 1;
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class CommitMlsGroupBody
    {
        [Required] public long Epoch { get; set; }
        [Required][MaxLength(128)] public string Reason { get; set; } = null!;
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class FanoutEnvelopeItemBody
    {
        [Required][MaxLength(512)] public string RecipientDeviceId { get; set; } = null!;
        [MaxLength(128)] public string? ClientMessageId { get; set; }
        [Required] public byte[] Ciphertext { get; set; } = [];
        public byte[]? Header { get; set; }
        public byte[]? Signature { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class FanoutMlsWelcomeBody
    {
        [Required] public Guid RecipientAccountId { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        [Required][MinLength(1)] public List<FanoutEnvelopeItemBody> Payloads { get; set; } = [];
    }

    public class MarkMlsReshareRequiredBody
    {
        [Required] public Guid TargetAccountId { get; set; }
        [Required][MaxLength(512)] public string TargetDeviceId { get; set; } = null!;
        [Required] public long Epoch { get; set; }
        [Required][MaxLength(128)] public string Reason { get; set; } = null!;
    }


    public class FanoutEnvelopeBody
    {
        [Required] public Guid RecipientAccountId { get; set; }
        public Guid? SessionId { get; set; }
        public SnE2eeEnvelopeType Type { get; set; } = SnE2eeEnvelopeType.PairwiseMessage;
        [MaxLength(256)] public string? GroupId { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool IncludeSenderCopy { get; set; }
        [Required][MinLength(1)] public List<FanoutEnvelopeItemBody> Payloads { get; set; } = [];
    }

    [HttpPut("mls/devices/me/kps")]
    public async Task<ActionResult<SnMlsKeyPackage>> PublishMlsKeyPackage([FromBody] PublishMlsKeyPackageBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized();

        var result = await e2eeModule.PublishMlsKeyPackageAsync(currentUser.Id, body.DeviceId, body.DeviceLabel,
            new PublishMlsKeyPackageRequest(body.KeyPackage, body.Ciphersuite, body.Meta));
        return Ok(result);
    }

    [HttpGet("mls/keys/{accountId:guid}/devices")]
    public async Task<ActionResult<List<MlsDeviceKeyPackageResponse>>> ListMlsKeyPackagesByDevice(
        Guid accountId,
        [FromQuery] bool consume = true
    )
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var result = await e2eeModule.ListMlsDeviceKeyPackagesAsync(accountId, currentUser.Id, consume);
        return Ok(result);
    }

    public class CheckMlsReadyResponse
    {
        public bool IsReady { get; set; }
        public int AvailableKeyPackages { get; set; }
    }

    public class BatchCheckMlsReadyRequest
    {
        [Required][MinLength(1)][MaxLength(100)] public List<Guid> AccountIds { get; set; } = [];
    }

    public class BatchCheckMlsReadyResponse
    {
        public List<MlsUserAvailability> Users { get; set; } = [];
    }

    public class MlsUserAvailability
    {
        public Guid AccountId { get; set; }
        public bool IsReady { get; set; }
        public int AvailableKeyPackages { get; set; }
    }

    [HttpPost("mls/users/ready/batch")]
    public async Task<ActionResult<BatchCheckMlsReadyResponse>> BatchCheckMlsUsersReady([FromBody] BatchCheckMlsReadyRequest body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var results = new List<MlsUserAvailability>();
        foreach (var accountId in body.AccountIds)
        {
            var packages = await e2eeModule.ListMlsDeviceKeyPackagesAsync(accountId, currentUser.Id, consume: false);
            results.Add(new MlsUserAvailability
            {
                AccountId = accountId,
                IsReady = packages.Count > 0,
                AvailableKeyPackages = packages.Count
            });
        }

        return Ok(new BatchCheckMlsReadyResponse { Users = results });
    }

    [HttpGet("mls/users/{accountId:guid}/ready")]
    public async Task<ActionResult<CheckMlsReadyResponse>> CheckMlsUserReady(Guid accountId)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();

        var packages = await e2eeModule.ListMlsDeviceKeyPackagesAsync(accountId, currentUser.Id, consume: false);
        return Ok(new CheckMlsReadyResponse
        {
            IsReady = packages.Count > 0,
            AvailableKeyPackages = packages.Count
        });
    }

    [HttpGet("mls/groups/{groupId}/devices/capable")]
    public async Task<ActionResult<List<MlsDeviceKeyPackageResponse>>> GetCapableDevices(string groupId)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var result = await e2eeModule.GetCapableDevicesAsync(groupId);
        return Ok(result);
    }

    [HttpPost("mls/groups/{groupId}/bootstrap")]
    public async Task<ActionResult<SnMlsGroupState>> BootstrapMlsGroup(string groupId, [FromBody] BootstrapMlsGroupBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var state = await e2eeModule.BootstrapMlsGroupAsync(currentUser.Id, new BootstrapMlsGroupRequest(
            groupId, body.Epoch, body.StateVersion, body.Meta
        ));
        return Ok(state);
    }

    [HttpPost("mls/groups/{groupId}/commit")]
    public async Task<ActionResult<SnMlsGroupState>> CommitMlsGroup(string groupId, [FromBody] CommitMlsGroupBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var state = await e2eeModule.CommitMlsGroupAsync(currentUser.Id, new CommitMlsGroupRequest(
            groupId, body.Epoch, body.Reason, body.Meta
        ));
        if (state is null) return NotFound();
        return Ok(state);
    }

    [HttpPost("mls/groups/{groupId}/welcome/fanout")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> FanoutMlsWelcome(string groupId, [FromBody] FanoutMlsWelcomeBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized();

        var senderDeviceId = ResolveDeviceId(currentSession);
        if (string.IsNullOrWhiteSpace(senderDeviceId))
            return BadRequest("Current session device id is missing.");

        var result = await e2eeModule.FanoutMlsWelcomeAsync(currentUser.Id, senderDeviceId,
            new FanoutMlsWelcomeRequest(
                groupId,
                body.RecipientAccountId,
                body.ExpiresAt,
                [.. body.Payloads.Select(x => new DeviceCiphertextEnvelope(
                    x.RecipientDeviceId,
                    x.ClientMessageId,
                    x.Ciphertext,
                    x.Header,
                    x.Signature,
                    x.Meta
                ))]
            ));
        return Ok(result);
    }

    [HttpPost("mls/groups/{groupId}/reshare-required")]
    public async Task<ActionResult<SnMlsDeviceMembership>> MarkMlsReshareRequired(
        string groupId,
        [FromBody] MarkMlsReshareRequiredBody body
    )
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var result = await e2eeModule.MarkMlsReshareRequiredAsync(currentUser.Id, new MarkMlsReshareRequiredRequest(
            groupId, body.TargetAccountId, body.TargetDeviceId, body.Epoch, body.Reason
        ));
        return Ok(result);
    }

    [HttpPost("mls/messages/fanout")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> SendMlsFanout([FromBody] FanoutEnvelopeBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized();

        var senderDeviceId = ResolveDeviceId(currentSession);
        if (string.IsNullOrWhiteSpace(senderDeviceId))
            return BadRequest("Current session device id is missing.");

        body.Type = SnE2eeEnvelopeType.MlsApplication;
        var envelopes = await e2eeModule.SendFanoutEnvelopesAsync(currentUser.Id, senderDeviceId,
            new SendE2eeFanoutRequest(
                body.RecipientAccountId,
                body.SessionId,
                body.Type,
                body.GroupId,
                body.ExpiresAt,
                body.IncludeSenderCopy,
                [.. body.Payloads.Select(x => new DeviceCiphertextEnvelope(
                    x.RecipientDeviceId,
                    x.ClientMessageId,
                    x.Ciphertext,
                    x.Header,
                    x.Signature,
                    x.Meta
                ))]
            ));
        return Ok(envelopes);
    }

    public class FanoutMlsCommitBody
    {
        [Required] public long Epoch { get; set; }
        [Required][MinLength(1)] public List<FanoutEnvelopeItemBody> Payloads { get; set; } = [];
    }

    [HttpPost("mls/groups/{groupId}/commit/fanout")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> FanoutMlsCommit(
        string groupId,
        [FromBody] FanoutMlsCommitBody body
    )
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized();

        var senderDeviceId = ResolveDeviceId(currentSession);
        if (string.IsNullOrWhiteSpace(senderDeviceId))
            return BadRequest("Current session device id is missing.");

        var currentState = await e2eeModule.GetMlsGroupStateByGroupIdAsync(groupId);
        if (currentState != null && body.Epoch != currentState.Epoch + 1)
        {
            return Conflict(new
            {
                code = "e2ee.mls_epoch_mismatch",
                currentEpoch = currentState.Epoch,
                requestedEpoch = body.Epoch
            });
        }

        var envelopes = await e2eeModule.FanoutMlsCommitAsync(
            currentUser.Id,
            senderDeviceId,
            new FanoutMlsCommitRequest(
                groupId,
                body.Epoch,
                [.. body.Payloads.Select(x => new DeviceCiphertextEnvelope(
                    x.RecipientDeviceId,
                    x.ClientMessageId,
                    x.Ciphertext,
                    x.Header,
                    x.Signature,
                    x.Meta
                ))]
            )
        );

        await e2eeModule.CommitMlsGroupAsync(currentUser.Id, new CommitMlsGroupRequest(
            groupId, body.Epoch, "member_add", null
        ));

        return Ok(envelopes);
    }

    [HttpGet("mls/envelopes/pending")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> GetMlsPendingByDevice(
        [FromQuery(Name = "deviceId")] string? deviceId,
        [FromQuery] int take = 100
    )
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession) return Unauthorized();

        var effectiveDeviceId = string.IsNullOrWhiteSpace(deviceId)
            ? ResolveDeviceId(currentSession)
            : deviceId;
        if (string.IsNullOrWhiteSpace(effectiveDeviceId))
            return BadRequest("device_id is required.");

        take = Math.Clamp(take, 1, 500);
        var envelopes = await e2eeModule.GetPendingEnvelopesByDeviceAsync(currentUser.Id, effectiveDeviceId, take);
        return Ok(envelopes);
    }

    [HttpPost("mls/envelopes/{envelopeId:guid}/ack")]
    public async Task<ActionResult<SnE2eeEnvelope>> AckMlsEnvelope(
        Guid envelopeId,
        [FromQuery(Name = "deviceId")] string? deviceId
    )
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized();

        var effectiveDeviceId = string.IsNullOrWhiteSpace(deviceId)
            ? ResolveDeviceId(currentSession)
            : deviceId;
        if (string.IsNullOrWhiteSpace(effectiveDeviceId))
            return BadRequest("device_id is required.");

        var message = await e2eeModule.AcknowledgeEnvelopeByDeviceAsync(currentUser.Id, effectiveDeviceId, envelopeId);
        if (message is null) return NotFound();
        return Ok(message);
    }

    [HttpPost("mls/devices/{deviceId}/revoke")]
    public async Task<ActionResult> RevokeMlsDevice(string deviceId)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();

        var revoked = await e2eeModule.RevokeDeviceAsync(currentUser.Id, deviceId);
        if (!revoked) return NotFound();
        return NoContent();
    }

    public class ResetMlsGroupBody
    {
        public long NewEpoch { get; set; }
        public long StateVersion { get; set; }
        [MaxLength(512)] public string? Reason { get; set; }
    }

    [HttpPost("mls/groups/{groupId}/reset")]
    public async Task<ActionResult<SnMlsGroupState>> ResetMlsGroup(string groupId, [FromBody] ResetMlsGroupBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var group = await e2eeModule.GetMlsGroupStateByGroupIdAsync(groupId);
        if (group is null) return NotFound();

        await e2eeModule.DeleteMlsGroupAsync(groupId);

        await e2eeModule.NotifyGroupResetAsync(groupId, body.Reason);

        var newState = await e2eeModule.CreateMlsGroupAsync(
            groupId,
            body.NewEpoch,
            body.StateVersion + 1
        );

        return Ok(newState);
    }
}
