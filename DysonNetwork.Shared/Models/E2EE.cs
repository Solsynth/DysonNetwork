using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum SnE2eeEnvelopeType
{
    PairwiseMessage = 0,
    SenderKeyDistribution = 1,
    SenderKeyMessage = 2,
    Control = 3
}

public enum SnE2eeEnvelopeStatus
{
    Pending = 0,
    Delivered = 1,
    Acknowledged = 2,
    Failed = 3
}

public class SnE2eeKeyBundle : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    [MaxLength(32)] public string Algorithm { get; set; } = "x25519";
    public byte[] IdentityKey { get; set; } = [];
    public int? SignedPreKeyId { get; set; }
    public byte[] SignedPreKey { get; set; } = [];
    public byte[] SignedPreKeySignature { get; set; } = [];
    public Instant? SignedPreKeyExpiresAt { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }

    [JsonIgnore] public List<SnE2eeOneTimePreKey> OneTimePreKeys { get; set; } = [];
}

public class SnE2eeOneTimePreKey : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid KeyBundleId { get; set; }
    [JsonIgnore] public SnE2eeKeyBundle KeyBundle { get; set; } = null!;

    public int KeyId { get; set; }
    public byte[] PublicKey { get; set; } = [];
    public bool IsClaimed { get; set; }
    public Instant? ClaimedAt { get; set; }
    public Guid? ClaimedByAccountId { get; set; }
}

public class SnE2eeSession : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountAId { get; set; }
    public Guid AccountBId { get; set; }
    public Guid InitiatedById { get; set; }
    public Instant? LastMessageAt { get; set; }
    [MaxLength(128)] public string? Hint { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
}

public class SnE2eeEnvelope : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SenderId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid? SessionId { get; set; }
    public SnE2eeEnvelopeType Type { get; set; } = SnE2eeEnvelopeType.PairwiseMessage;
    [MaxLength(256)] public string? GroupId { get; set; }
    [MaxLength(128)] public string? ClientMessageId { get; set; }
    public long Sequence { get; set; }

    public byte[] Ciphertext { get; set; } = [];
    public byte[]? Header { get; set; }
    public byte[]? Signature { get; set; }

    public SnE2eeEnvelopeStatus DeliveryStatus { get; set; } = SnE2eeEnvelopeStatus.Pending;
    public Instant? DeliveredAt { get; set; }
    public Instant? AckedAt { get; set; }
    public Instant? ExpiresAt { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
}
