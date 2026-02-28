using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Pass.E2EE;

[ApiController]
[Route("/api/e2ee")]
[Authorize]
public class E2eeController(IGroupE2eeModule e2eeModule) : ControllerBase
{
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
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();

        var bundle = await e2eeModule.UpsertKeyBundleAsync(currentUser.Id, new UpsertE2eeKeyBundleRequest(
            body.Algorithm,
            body.IdentityKey,
            body.SignedPreKeyId,
            body.SignedPreKey,
            body.SignedPreKeySignature,
            body.SignedPreKeyExpiresAt,
            body.OneTimePreKeys?.Select(x => new UpsertE2eeOneTimePreKey(x.KeyId, x.PublicKey)).ToList(),
            body.Meta
        ));

        return Ok(bundle);
    }

    [HttpGet("keys/me")]
    public async Task<ActionResult<E2eePublicKeyBundleResponse>> GetMyPublicBundle()
    {
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();

        var bundle = await e2eeModule.GetPublicBundleAsync(currentUser.Id, currentUser.Id, consumeOneTimePreKey: false);
        if (bundle is null) return NotFound();
        return Ok(bundle);
    }

    [HttpGet("keys/{accountId:guid}/bundle")]
    public async Task<ActionResult<E2eePublicKeyBundleResponse>> GetPublicBundle(
        Guid accountId,
        [FromQuery] bool consumeOneTimePreKey = true
    )
    {
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();

        var bundle = await e2eeModule.GetPublicBundleAsync(accountId, currentUser.Id, consumeOneTimePreKey);
        if (bundle is null) return NotFound();
        return Ok(bundle);
    }

    [HttpPost("sessions/{peerId:guid}")]
    public async Task<ActionResult<SnE2eeSession>> EnsureSession(Guid peerId, [FromBody] EnsureSessionBody body)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();
        if (peerId == currentUser.Id) return BadRequest("Cannot create session with yourself.");

        var session = await e2eeModule.EnsureSessionAsync(
            currentUser.Id,
            peerId,
            new EnsureE2eeSessionRequest(body.Hint, body.Meta)
        );
        return Ok(session);
    }

    [HttpPost("messages")]
    public async Task<ActionResult<SnE2eeEnvelope>> SendEnvelope([FromBody] SendEnvelopeBody body)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();
        if (body.RecipientId == currentUser.Id) return BadRequest("Cannot send E2EE message to yourself.");

        var envelope = await e2eeModule.SendEnvelopeAsync(currentUser.Id, new SendE2eeEnvelopeRequest(
            body.RecipientId,
            body.SessionId,
            body.Type,
            body.GroupId,
            body.ClientMessageId,
            body.Ciphertext,
            body.Header,
            body.Signature,
            body.ExpiresAt,
            body.Meta
        ));
        return Ok(envelope);
    }

    [HttpPost("groups/sender-key/distribute")]
    public async Task<ActionResult<object>> DistributeSenderKey([FromBody] DistributeSenderKeyBody body)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();

        var sent = await e2eeModule.DistributeSenderKeyAsync(currentUser.Id, new DistributeSenderKeyRequest(
            body.GroupId,
            body.Items.Select(x => new SenderKeyDistributionItem(
                x.RecipientId,
                x.Ciphertext,
                x.Header,
                x.Signature,
                x.ClientMessageId,
                x.Meta
            )).ToList(),
            body.ExpiresAt
        ));
        return Ok(new { sent });
    }

    [HttpGet("messages/pending")]
    public async Task<ActionResult<List<SnE2eeEnvelope>>> GetPendingMessages([FromQuery] int take = 100)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();

        take = Math.Clamp(take, 1, 500);
        var messages = await e2eeModule.GetPendingEnvelopesAsync(currentUser.Id, take);
        return Ok(messages);
    }

    [HttpPost("messages/{envelopeId:guid}/ack")]
    public async Task<ActionResult<SnE2eeEnvelope>> AckMessage(Guid envelopeId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        if (currentUser is null) return Unauthorized();

        var message = await e2eeModule.AcknowledgeEnvelopeAsync(currentUser.Id, envelopeId);
        if (message is null) return NotFound();
        return Ok(message);
    }
}
