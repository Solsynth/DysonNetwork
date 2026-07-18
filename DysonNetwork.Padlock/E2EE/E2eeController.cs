using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnAuthSession = DysonNetwork.Shared.Models.SnAuthSession;

namespace DysonNetwork.Padlock.E2EE;

[ApiController]
[Route("/api/e2ee")]
[Authorize]
[ApiFeature("e2ee", Revision = 1)]
[ApiFeature("e2ee.mls", Revision = 1)]
public class E2EeController(IE2EeModule e2EeModule) : ControllerBase
{

    public class PublishMlsKeyPackageBody
    {
        [Required] public byte[] KeyPackage { get; set; } = [];
        [MaxLength(128)] public string Ciphersuite { get; set; } = "MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519";
        [Required][MaxLength(1024)] public string DeviceId { get; set; } = null!;
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
        [MaxLength(128)] public string Reason { get; set; } = "client_commit";
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class FanoutEnvelopeItemBody
    {
        [MaxLength(512)] public string? RecipientDeviceId { get; set; }
        [MaxLength(128)] public string? ClientMessageId { get; set; }
        [Required] public byte[] Ciphertext { get; set; } = [];
        public byte[]? Header { get; set; }
        public byte[]? Signature { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class FanoutMlsWelcomeBody
    {
        public Guid? RecipientAccountId { get; set; }
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

    public class UploadGroupInfoBody
    {
        [Required] public long Epoch { get; set; }
        [Required] public byte[] GroupInfo { get; set; } = [];
        [Required] public byte[] RatchetTree { get; set; } = [];
    }

    private static string GetCommitReason(Dictionary<string, object>? meta, string fallback)
    {
        if (meta is not null &&
            meta.TryGetValue("reason", out var reason) &&
            !string.IsNullOrWhiteSpace(reason?.ToString()))
            return reason.ToString()!;

        return fallback;
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var result = await e2EeModule.PublishMlsKeyPackageAsync(currentUser.Id, body.DeviceId, body.DeviceLabel,
            new PublishMlsKeyPackageRequest(body.KeyPackage, body.Ciphersuite, body.Meta));
        return Ok(result);
    }

    [HttpGet("mls/kp/status")]
    public async Task<ActionResult<MlsKeyPackageStatusResponse>> GetMlsKeyPackageStatus()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var result = await e2EeModule.GetMlsKeyPackageStatusAsync(currentUser.Id);
        return Ok(result);
    }

    [HttpGet("mls/keys/{accountId:guid}/devices")]
    public async Task<ActionResult<List<MlsDeviceKeyPackageResponse>>> ListMlsKeyPackagesByDevice(
        Guid accountId,
        [FromQuery] bool consume = true
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var result = await e2EeModule.ListMlsDeviceKeyPackagesAsync(accountId, currentUser.Id, consume);
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var results = new List<MlsUserAvailability>();
        foreach (var accountId in body.AccountIds)
        {
            var packages = await e2EeModule.ListMlsDeviceKeyPackagesAsync(accountId, currentUser.Id, consume: false);
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
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var packages = await e2EeModule.ListMlsDeviceKeyPackagesAsync(accountId, currentUser.Id, consume: false);
        return Ok(new CheckMlsReadyResponse
        {
            IsReady = packages.Count > 0,
            AvailableKeyPackages = packages.Count
        });
    }

    [HttpGet("mls/groups/{groupId}/devices/capable")]
    public async Task<ActionResult<List<MlsDeviceKeyPackageResponse>>> GetCapableDevices(string groupId)
    {
        var result = await e2EeModule.GetCapableDevicesAsync(groupId);
        return Ok(result);
    }

    [HttpPost("mls/groups/{groupId}/bootstrap")]
    public async Task<ActionResult<SnMlsGroupState>> BootstrapMlsGroup(string groupId, [FromBody] BootstrapMlsGroupBody body)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var state = await e2EeModule.BootstrapMlsGroupAsync(currentUser.Id, new BootstrapMlsGroupRequest(
            groupId, body.Epoch, body.StateVersion, body.Meta
        ));
        return Ok(state);
    }

    [HttpPost("mls/groups/{groupId}/commit")]
    public async Task<ActionResult<SnMlsGroupState>> CommitMlsGroup(string groupId, [FromBody] CommitMlsGroupBody body)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var state = await e2EeModule.CommitMlsGroupAsync(currentUser.Id, new CommitMlsGroupRequest(
            groupId, body.Epoch, body.Reason, body.Meta
        ));
        if (state is null) return NotFound(new ApiError { Code = "E2EE_GROUP_NOT_FOUND", Message = "MLS group was not found.", Status = 404 });
        return Ok(state);
    }

    [HttpPost("mls/groups/{groupId}/welcome/fanout")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> FanoutMlsWelcome(
        string groupId,
        [FromBody] FanoutMlsWelcomeBody body,
        [FromHeader(Name = "X-Device-Id")] string? senderDeviceId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (string.IsNullOrWhiteSpace(senderDeviceId))
            return BadRequest(new ApiError { Code = "E2EE_DEVICE_ID_REQUIRED", Message = "X-Device-Id header is required.", Status = 400 });
        if (!await e2EeModule.IsMlsGroupMemberAsync(currentUser.Id, senderDeviceId, groupId))
            return Forbid();

        var result = await e2EeModule.FanoutMlsWelcomeAsync(currentUser.Id, senderDeviceId,
            new FanoutMlsWelcomeRequest(
                groupId,
                body.RecipientAccountId,
                body.ExpiresAt,
                [.. body.Payloads.Select(x => new DeviceCiphertextEnvelope(
                    x.RecipientDeviceId ?? string.Empty,
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var result = await e2EeModule.MarkMlsReshareRequiredAsync(currentUser.Id, new MarkMlsReshareRequiredRequest(
            groupId, body.TargetAccountId, body.TargetDeviceId, body.Epoch, body.Reason
        ));
        return Ok(result);
    }

    [HttpGet("mls/devices/me/reshare-required")]
    public async Task<ActionResult<List<SnMlsDeviceMembership>>> GetMyDeviceReshareStatus(
        [FromHeader(Name = "X-Device-Id")] string? deviceId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest(new ApiError { Code = "E2EE_DEVICE_ID_REQUIRED", Message = "X-Device-Id header is required.", Status = 400 });

        var result = await e2EeModule.GetDeviceMlsReshareStatusAsync(currentUser.Id, deviceId);
        return Ok(result);
    }

    [HttpPost("mls/devices/me/reshare-required/{groupId}/complete")]
    public async Task<ActionResult> CompleteMyDeviceReshare(
        string groupId,
        [FromHeader(Name = "X-Device-Id")] string? deviceId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest(new ApiError { Code = "E2EE_DEVICE_ID_REQUIRED", Message = "X-Device-Id header is required.", Status = 400 });

        var result = await e2EeModule.CompleteMlsReshareAsync(currentUser.Id, deviceId, groupId);
        if (!result) return NotFound(new ApiError { Code = "E2EE_RESHARE_NOT_FOUND", Message = "Device reshare status was not found.", Status = 404 });
        return NoContent();
    }

    [HttpPut("mls/groups/{groupId}/groupinfo")]
    public async Task<ActionResult> UploadGroupInfo(
        string groupId,
        [FromBody] UploadGroupInfoBody body,
        [FromHeader(Name = "X-Device-Id")] string? deviceId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest(new ApiError { Code = "E2EE_DEVICE_ID_REQUIRED", Message = "X-Device-Id header is required.", Status = 400 });
        if (!await e2EeModule.IsMlsGroupMemberAsync(currentUser.Id, deviceId, groupId))
            return Forbid();

        var result = await e2EeModule.UploadGroupInfoAsync(
            groupId,
            body.GroupInfo,
            body.RatchetTree,
            body.Epoch);
        if (!result.Success)
            return Conflict(new ApiError
            {
                Code = "E2EE_MLS_EPOCH_MISMATCH",
                Message = "Epoch mismatch when uploading group info.",
                Status = 409,
                Detail = $"Current epoch: {result.Epoch}, requested epoch: {body.Epoch}"
            });
        return Ok(new
        {
            result.Success,
            result.GroupId,
            result.Epoch
        });
    }

    [HttpGet("mls/groups/{groupId}/groupinfo")]
    public async Task<ActionResult> GetGroupInfo(
        string groupId,
        [FromHeader(Name = "X-Device-Id")] string? deviceId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest(new ApiError { Code = "E2EE_DEVICE_ID_REQUIRED", Message = "X-Device-Id header is required.", Status = 400 });
        if (!await e2EeModule.IsMlsGroupMemberAsync(currentUser.Id, deviceId, groupId))
            return Forbid();

        var state = await e2EeModule.GetMlsGroupStateByGroupIdAsync(groupId);
        if (state is null) return NotFound(new ApiError { Code = "E2EE_GROUP_NOT_FOUND", Message = "MLS group was not found.", Status = 404 });

        return Ok(new
        {
            groupId = state.MlsGroupId,
            epoch = state.Epoch,
            groupInfo = state.GroupInfo,
            ratchetTree = state.RatchetTree
        });
    }

    [HttpPost("mls/messages/fanout")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> SendMlsFanout(
        [FromBody] FanoutEnvelopeBody body,
        [FromHeader(Name = "X-Device-Id")] string? senderDeviceId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (string.IsNullOrWhiteSpace(senderDeviceId))
            return BadRequest(new ApiError { Code = "E2EE_DEVICE_ID_REQUIRED", Message = "X-Device-Id header is required.", Status = 400 });
        if (string.IsNullOrWhiteSpace(body.GroupId) ||
            !await e2EeModule.IsMlsGroupMemberAsync(currentUser.Id, senderDeviceId, body.GroupId))
            return Forbid();

        body.Type = SnE2eeEnvelopeType.MlsApplication;
        var envelopes = await e2EeModule.SendFanoutEnvelopesAsync(currentUser.Id, senderDeviceId,
            new SendE2EeFanoutRequest(
                body.RecipientAccountId,
                body.SessionId,
                body.Type,
                body.GroupId,
                body.ExpiresAt,
                body.IncludeSenderCopy,
                [.. body.Payloads.Select(x => new DeviceCiphertextEnvelope(
                    x.RecipientDeviceId ?? string.Empty,
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
        [Required] public byte[] Ciphertext { get; set; } = [];
        public byte[]? Header { get; set; }
        public byte[]? Signature { get; set; }
        [MaxLength(128)] public string? ClientMessageId { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class FanoutMlsGroupMessageBody
    {
        [Required] public byte[] Ciphertext { get; set; } = [];
        public byte[]? Header { get; set; }
        public byte[]? Signature { get; set; }
        [MaxLength(128)] public string? ClientMessageId { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    [HttpPost("mls/groups/{groupId}/commit/fanout")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> FanoutMlsCommit(
        string groupId,
        [FromBody] FanoutMlsCommitBody body,
        [FromHeader(Name = "X-Device-Id")] string? senderDeviceId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (string.IsNullOrWhiteSpace(senderDeviceId))
            return BadRequest(new ApiError { Code = "E2EE_DEVICE_ID_REQUIRED", Message = "X-Device-Id header is required.", Status = 400 });
        if (!await e2EeModule.IsMlsGroupMemberAsync(currentUser.Id, senderDeviceId, groupId))
            return Forbid();

        var currentState = await e2EeModule.GetMlsGroupStateByGroupIdAsync(groupId);
        if (currentState != null && body.Epoch <= currentState.Epoch)
        {
            return Conflict(new ApiError
            {
                Code = "E2EE_MLS_STALE_EPOCH",
                Message = "Stale epoch for MLS commit.",
                Status = 409,
                Detail = $"Current epoch: {currentState.Epoch}, requested epoch: {body.Epoch}"
            });
        }

        if (currentState != null && body.Epoch != currentState.Epoch + 1)
        {
            return Conflict(new ApiError
            {
                Code = "E2EE_MLS_EPOCH_MISMATCH",
                Message = "Epoch mismatch for MLS commit.",
                Status = 409,
                Detail = $"Current epoch: {currentState.Epoch}, requested epoch: {body.Epoch}"
            });
        }

        var envelopes = await e2EeModule.FanoutMlsCommitAsync(
            currentUser.Id,
            senderDeviceId,
            new FanoutMlsCommitRequest(
                groupId,
                body.Epoch,
                body.Ciphertext,
                body.Header,
                body.Signature,
                body.ClientMessageId,
                body.Meta
            )
        );

        var state = await e2EeModule.CommitMlsGroupAsync(currentUser.Id, new CommitMlsGroupRequest(
            groupId, body.Epoch, GetCommitReason(body.Meta, "member_add"), body.Meta
        ));
        if (state is null) return NotFound(new ApiError { Code = "E2EE_GROUP_NOT_FOUND", Message = "MLS group was not found.", Status = 404 });

        return Ok(envelopes);
    }

    [HttpPost("mls/groups/{groupId}/messages/fanout")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> FanoutMlsMessageToGroup(
        string groupId,
        [FromBody] FanoutMlsGroupMessageBody body,
        [FromHeader(Name = "X-Device-Id")] string? senderDeviceId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (string.IsNullOrWhiteSpace(senderDeviceId))
            return BadRequest(new ApiError { Code = "E2EE_DEVICE_ID_REQUIRED", Message = "X-Device-Id header is required.", Status = 400 });
        if (!await e2EeModule.IsMlsGroupMemberAsync(currentUser.Id, senderDeviceId, groupId))
            return Forbid();

        var envelopes = await e2EeModule.FanoutMlsMessageToGroupAsync(
            currentUser.Id,
            senderDeviceId,
            new FanoutMlsGroupMessageRequest(
                groupId,
                body.Ciphertext,
                body.Header,
                body.Signature,
                body.ClientMessageId,
                body.Meta
            )
        );

        return Ok(envelopes);
    }

    [HttpGet("mls/envelopes/pending")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> GetMlsPendingByDevice(
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        [FromQuery] int take = 100
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest(new ApiError { Code = "E2EE_DEVICE_ID_REQUIRED", Message = "X-Device-Id header is required.", Status = 400 });

        take = Math.Clamp(take, 1, 500);
        var envelopes = await e2EeModule.GetPendingEnvelopesByDeviceAsync(currentUser.Id, deviceId, take);
        return Ok(envelopes);
    }

    [HttpPost("mls/envelopes/{envelopeId:guid}/ack")]
    public async Task<ActionResult<SnE2eeEnvelope>> AckMlsEnvelope(
        Guid envelopeId,
        [FromHeader(Name = "X-Device-Id")] string? deviceId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest(new ApiError { Code = "E2EE_DEVICE_ID_REQUIRED", Message = "X-Device-Id header is required.", Status = 400 });

        var message = await e2EeModule.AcknowledgeEnvelopeByDeviceAsync(currentUser.Id, deviceId, envelopeId);
        if (message is null) return NotFound(new ApiError { Code = "E2EE_ENVELOPE_NOT_FOUND", Message = "Envelope was not found.", Status = 404 });
        return Ok(message);
    }

    [HttpPost("mls/devices/{deviceId}/revoke")]
    public async Task<ActionResult> RevokeMlsDevice(string deviceId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var revoked = await e2EeModule.RevokeDeviceAsync(currentUser.Id, deviceId);
        if (!revoked) return NotFound(new ApiError { Code = "E2EE_DEVICE_NOT_FOUND", Message = "Device was not found.", Status = 404 });
        return NoContent();
    }

    public class AddMlsDeviceMembershipBody
    {
        [Required] public string GroupId { get; set; } = null!;
        [Required] public long Epoch { get; set; }
    }

    [HttpPost("mls/devices/{deviceId}/membership")]
    public async Task<ActionResult<SnMlsDeviceMembership>> AddMlsDeviceMembership(
        string deviceId,
        [FromBody] AddMlsDeviceMembershipBody body
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var membership = await e2EeModule.AddMlsDeviceMembershipAsync(
            currentUser.Id,
            deviceId,
            body.GroupId,
            body.Epoch
        );
        return Ok(membership);
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var group = await e2EeModule.GetMlsGroupStateByGroupIdAsync(groupId);
        if (group is null) return NotFound(new ApiError { Code = "E2EE_GROUP_NOT_FOUND", Message = "MLS group was not found.", Status = 404 });

        await e2EeModule.MarkAllDevicesReshareRequiredAsync(groupId, body.Reason ?? "group_reset");
        await e2EeModule.NotifyGroupResetAsync(groupId, body.Reason);
        await e2EeModule.DeleteMlsGroupAsync(groupId);

        var newState = await e2EeModule.CreateMlsGroupAsync(
            groupId,
            body.NewEpoch,
            body.StateVersion + 1
        );

        return Ok(newState);
    }
}
