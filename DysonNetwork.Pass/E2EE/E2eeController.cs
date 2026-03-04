using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnAuthSession = DysonNetwork.Shared.Models.SnAuthSession;

namespace DysonNetwork.Pass.E2EE;

[ApiController]
[Route("/api/e2ee")]
[Authorize]
public class E2eeController(IGroupE2eeModule e2eeModule) : ControllerBase
{
    private const string AbilityHeader = "X-Client-Ability";
    private const string MlsAbilityToken = "chat-mls-v1";
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

    private ActionResult LegacyEndpointRemoved()
    {
        return StatusCode(410, new
        {
            code = "e2ee.legacy_endpoint_removed",
            error = "Legacy E2EE endpoint removed. Use /api/e2ee/mls/* endpoints."
        });
    }

    public class UploadKeyBundleBody
    {
        [Required] [MaxLength(32)] public string Algorithm { get; set; } = "x25519";
        [Required] public byte[] IdentityKey { get; set; } = [];
        public int? SignedPreKeyId { get; set; }
        [Required] public byte[] SignedPreKey { get; set; } = [];
        [Required] public byte[] SignedPreKeySignature { get; set; } = [];
        public DateTimeOffset? SignedPreKeyExpiresAt { get; set; }
        public List<OneTimePreKeyBody>? OneTimePreKeys { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class UploadDeviceBundleBody : UploadKeyBundleBody
    {
        [MaxLength(1024)] public string? DeviceLabel { get; set; }
    }

    public class OneTimePreKeyBody
    {
        [Required] public int KeyId { get; set; }
        [Required] public byte[] PublicKey { get; set; } = [];
    }

    public class EnsureSessionBody
    {
        [MaxLength(128)] public string? Hint { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class SendEnvelopeBody
    {
        [Required] public Guid RecipientId { get; set; }
        public Guid? SessionId { get; set; }
        public SnE2eeEnvelopeType Type { get; set; } = SnE2eeEnvelopeType.PairwiseMessage;
        [MaxLength(256)] public string? GroupId { get; set; }
        [MaxLength(128)] public string? ClientMessageId { get; set; }
        [Required] public byte[] Ciphertext { get; set; } = [];
        public byte[]? Header { get; set; }
        public byte[]? Signature { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class FanoutEnvelopeBody
    {
        [Required] public Guid RecipientAccountId { get; set; }
        public Guid? SessionId { get; set; }
        public SnE2eeEnvelopeType Type { get; set; } = SnE2eeEnvelopeType.PairwiseMessage;
        [MaxLength(256)] public string? GroupId { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool IncludeSenderCopy { get; set; }
        [Required] [MinLength(1)] public List<FanoutEnvelopeItemBody> Payloads { get; set; } = [];
    }

    public class FanoutEnvelopeItemBody
    {
        [Required] [MaxLength(512)] public string RecipientDeviceId { get; set; } = null!;
        [MaxLength(128)] public string? ClientMessageId { get; set; }
        [Required] public byte[] Ciphertext { get; set; } = [];
        public byte[]? Header { get; set; }
        public byte[]? Signature { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class PublishMlsKeyPackageBody
    {
        [Required] public byte[] KeyPackage { get; set; } = [];
        [MaxLength(128)] public string Ciphersuite { get; set; } = "MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519";
        [MaxLength(1024)] public string? DeviceLabel { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class BootstrapMlsGroupBody
    {
        [Required] public Guid ChatRoomId { get; set; }
        [Required] [MaxLength(256)] public string MlsGroupId { get; set; } = null!;
        [Required] public long Epoch { get; set; }
        public long StateVersion { get; set; } = 1;
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class CommitMlsGroupBody
    {
        [Required] public Guid ChatRoomId { get; set; }
        [Required] [MaxLength(256)] public string MlsGroupId { get; set; } = null!;
        [Required] public long Epoch { get; set; }
        [Required] [MaxLength(128)] public string Reason { get; set; } = null!;
        public Dictionary<string, object>? Meta { get; set; }
    }

    public class FanoutMlsWelcomeBody
    {
        [Required] public Guid ChatRoomId { get; set; }
        [Required] [MaxLength(256)] public string MlsGroupId { get; set; } = null!;
        [Required] public Guid RecipientAccountId { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        [Required] [MinLength(1)] public List<FanoutEnvelopeItemBody> Payloads { get; set; } = [];
    }

    public class MarkMlsReshareRequiredBody
    {
        [Required] public Guid ChatRoomId { get; set; }
        [Required] [MaxLength(256)] public string MlsGroupId { get; set; } = null!;
        [Required] public Guid TargetAccountId { get; set; }
        [Required] [MaxLength(512)] public string TargetDeviceId { get; set; } = null!;
        [Required] public long Epoch { get; set; }
        [Required] [MaxLength(128)] public string Reason { get; set; } = null!;
    }

    public class DistributeSenderKeyBody
    {
        [Required] [MaxLength(256)] public string GroupId { get; set; } = null!;
        [Required] [MinLength(1)] public List<SenderKeyEnvelopeBody> Items { get; set; } = [];
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    public class SenderKeyEnvelopeBody
    {
        [Required] public Guid RecipientId { get; set; }
        [Required] public byte[] Ciphertext { get; set; } = [];
        public byte[]? Header { get; set; }
        public byte[]? Signature { get; set; }
        [MaxLength(128)] public string? ClientMessageId { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    [HttpPost("keys/upload")]
    public async Task<ActionResult<SnE2eeKeyBundle>> UploadKeyBundle([FromBody] UploadKeyBundleBody body)
    {
        return LegacyEndpointRemoved();
    }

    [HttpPut("devices/me/bundle")]
    public async Task<ActionResult<SnE2eeKeyBundle>> UploadDeviceBundle([FromBody] UploadDeviceBundleBody body)
    {
        return LegacyEndpointRemoved();
    }

    [HttpGet("keys/me")]
    public async Task<ActionResult<E2eePublicKeyBundleResponse>> GetMyPublicBundle()
    {
        return LegacyEndpointRemoved();
    }

    [HttpGet("keys/{accountId:guid}/bundle")]
    public async Task<ActionResult<E2eePublicKeyBundleResponse>> GetPublicBundle(
        Guid accountId,
        [FromQuery] bool consumeOneTimePreKey = true
    )
    {
        return LegacyEndpointRemoved();
    }

    [HttpGet("keys/{accountId:guid}/devices")]
    public async Task<ActionResult<List<E2eeDevicePublicBundleResponse>>> GetPublicBundlesByDevice(
        Guid accountId,
        [FromQuery] bool consumeOneTimePreKey = true
    )
    {
        return LegacyEndpointRemoved();
    }

    [HttpPost("sessions/{peerId:guid}")]
    public async Task<ActionResult<SnE2eeSession>> EnsureSession(Guid peerId, [FromBody] EnsureSessionBody body)
    {
        return LegacyEndpointRemoved();
    }

    [HttpPost("messages")]
    public async Task<ActionResult<SnE2eeEnvelope>> SendEnvelope([FromBody] SendEnvelopeBody body)
    {
        return LegacyEndpointRemoved();
    }

    [HttpPost("messages/fanout")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> SendFanout([FromBody] FanoutEnvelopeBody body)
    {
        return LegacyEndpointRemoved();
    }

    [HttpPost("groups/sender-key/distribute")]
    public async Task<ActionResult<object>> DistributeSenderKey([FromBody] DistributeSenderKeyBody body)
    {
        return LegacyEndpointRemoved();
    }

    [HttpGet("messages/pending")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> GetPendingMessages([FromQuery] int take = 100)
    {
        return LegacyEndpointRemoved();
    }

    [HttpGet("envelopes/pending")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> GetPendingByDevice(
        [FromQuery(Name = "device_id")] string? deviceId,
        [FromQuery] int take = 100
    )
    {
        return LegacyEndpointRemoved();
    }

    [HttpPost("messages/{envelopeId:guid}/ack")]
    public async Task<ActionResult<SnE2eeEnvelope>> AckMessage(Guid envelopeId)
    {
        return LegacyEndpointRemoved();
    }

    [HttpPost("envelopes/{envelopeId:guid}/ack")]
    public async Task<ActionResult<SnE2eeEnvelope>> AckMessageByDevice(
        Guid envelopeId,
        [FromQuery(Name = "device_id")] string? deviceId
    )
    {
        return LegacyEndpointRemoved();
    }

    [HttpPost("devices/{deviceId}/revoke")]
    public async Task<ActionResult> RevokeDevice(string deviceId)
    {
        return LegacyEndpointRemoved();
    }

    [HttpPut("mls/devices/me/key-packages")]
    public async Task<ActionResult<SnMlsKeyPackage>> PublishMlsKeyPackage([FromBody] PublishMlsKeyPackageBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        var currentSession = HttpContext.Items["CurrentSession"] as SnAuthSession;
        if (currentUser is null || currentSession is null) return Unauthorized();

        var deviceId = ResolveDeviceId(currentSession);
        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest("Current session device id is missing.");

        var result = await e2eeModule.PublishMlsKeyPackageAsync(currentUser.Id, deviceId, body.DeviceLabel,
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
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();

        var result = await e2eeModule.ListMlsDeviceKeyPackagesAsync(accountId, currentUser.Id, consume);
        return Ok(result);
    }

    [HttpPost("mls/groups/{roomId:guid}/bootstrap")]
    public async Task<ActionResult<SnMlsGroupState>> BootstrapMlsGroup(Guid roomId, [FromBody] BootstrapMlsGroupBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();
        if (roomId != body.ChatRoomId) return BadRequest("Room id mismatch.");

        var state = await e2eeModule.BootstrapMlsGroupAsync(currentUser.Id, new BootstrapMlsGroupRequest(
            body.ChatRoomId, body.MlsGroupId, body.Epoch, body.StateVersion, body.Meta
        ));
        return Ok(state);
    }

    [HttpPost("mls/groups/{roomId:guid}/commit")]
    public async Task<ActionResult<SnMlsGroupState>> CommitMlsGroup(Guid roomId, [FromBody] CommitMlsGroupBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();
        if (roomId != body.ChatRoomId) return BadRequest("Room id mismatch.");

        var state = await e2eeModule.CommitMlsGroupAsync(currentUser.Id, new CommitMlsGroupRequest(
            body.ChatRoomId, body.MlsGroupId, body.Epoch, body.Reason, body.Meta
        ));
        if (state is null) return NotFound();
        return Ok(state);
    }

    [HttpPost("mls/groups/{roomId:guid}/welcome/fanout")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> FanoutMlsWelcome(Guid roomId, [FromBody] FanoutMlsWelcomeBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        var currentSession = HttpContext.Items["CurrentSession"] as SnAuthSession;
        if (currentUser is null || currentSession is null) return Unauthorized();
        if (roomId != body.ChatRoomId) return BadRequest("Room id mismatch.");

        var senderDeviceId = ResolveDeviceId(currentSession);
        if (string.IsNullOrWhiteSpace(senderDeviceId))
            return BadRequest("Current session device id is missing.");

        var result = await e2eeModule.FanoutMlsWelcomeAsync(currentUser.Id, senderDeviceId,
            new FanoutMlsWelcomeRequest(
                body.ChatRoomId,
                body.MlsGroupId,
                body.RecipientAccountId,
                body.ExpiresAt,
                body.Payloads.Select(x => new DeviceCiphertextEnvelope(
                    x.RecipientDeviceId,
                    x.ClientMessageId,
                    x.Ciphertext,
                    x.Header,
                    x.Signature,
                    x.Meta
                )).ToList()
            ));
        return Ok(result);
    }

    [HttpPost("mls/groups/{roomId:guid}/reshare-required")]
    public async Task<ActionResult<SnMlsDeviceMembership>> MarkMlsReshareRequired(
        Guid roomId,
        [FromBody] MarkMlsReshareRequiredBody body
    )
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();
        if (roomId != body.ChatRoomId) return BadRequest("Room id mismatch.");

        var result = await e2eeModule.MarkMlsReshareRequiredAsync(currentUser.Id, new MarkMlsReshareRequiredRequest(
            body.ChatRoomId, body.MlsGroupId, body.TargetAccountId, body.TargetDeviceId, body.Epoch, body.Reason
        ));
        return Ok(result);
    }

    [HttpPost("mls/messages/fanout")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> SendMlsFanout([FromBody] FanoutEnvelopeBody body)
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        var currentSession = HttpContext.Items["CurrentSession"] as SnAuthSession;
        if (currentUser is null || currentSession is null) return Unauthorized();

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
                body.Payloads.Select(x => new DeviceCiphertextEnvelope(
                    x.RecipientDeviceId,
                    x.ClientMessageId,
                    x.Ciphertext,
                    x.Header,
                    x.Signature,
                    x.Meta
                )).ToList()
            ));
        return Ok(envelopes);
    }

    [HttpGet("mls/envelopes/pending")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> GetMlsPendingByDevice(
        [FromQuery(Name = "device_id")] string? deviceId,
        [FromQuery] int take = 100
    )
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        var currentSession = HttpContext.Items["CurrentSession"] as SnAuthSession;
        if (currentUser is null || currentSession is null) return Unauthorized();

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
        [FromQuery(Name = "device_id")] string? deviceId
    )
    {
        if (EnsureMlsAbility() is { } abilityError) return abilityError;
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        var currentSession = HttpContext.Items["CurrentSession"] as SnAuthSession;
        if (currentUser is null || currentSession is null) return Unauthorized();

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
}
