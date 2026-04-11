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
    Control = 3,
    MlsCommit = 4,
    MlsWelcome = 5,
    MlsApplication = 6,
    MlsProposal = 7,
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
    [MaxLength(512)] public string DeviceId { get; set; } = string.Empty;

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
    public Guid AccountId { get; set; }
    [MaxLength(512)] public string DeviceId { get; set; } = string.Empty;

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
    [MaxLength(512)] public string? SenderDeviceId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid RecipientAccountId { get; set; }
    [MaxLength(512)] public string? RecipientDeviceId { get; set; }
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
    public bool LegacyAccountScoped { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
}

public class SnE2eeDevice : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;
    [MaxLength(512)] public string DeviceId { get; set; } = string.Empty;
    [MaxLength(1024)] public string? DeviceLabel { get; set; }
    public bool IsRevoked { get; set; }
    public Instant? RevokedAt { get; set; }
    public Instant? LastBundleAt { get; set; }
}

public class SnMlsKeyPackage : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;
    [MaxLength(512)] public string DeviceId { get; set; } = string.Empty;
    [MaxLength(1024)] public string? DeviceLabel { get; set; }
    public byte[] KeyPackage { get; set; } = [];
    [MaxLength(128)] public string Ciphersuite { get; set; } = "MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519";
    public bool IsConsumed { get; set; }
    public Instant? ConsumedAt { get; set; }
    public Guid? ConsumedByAccountId { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
}

public class SnMlsGroupState : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(256)] public string MlsGroupId { get; set; } = string.Empty;
    public long Epoch { get; set; }
    public long StateVersion { get; set; }
    public Instant? LastCommitAt { get; set; }
    public byte[] GroupInfo { get; set; } = [];
    public byte[] RatchetTree { get; set; } = [];
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
}

public class SnMlsDeviceMembership : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(256)] public string MlsGroupId { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    [MaxLength(512)] public string DeviceId { get; set; } = string.Empty;
    public long JoinedEpoch { get; set; }
    public long? LastSeenEpoch { get; set; }
    public Instant? LastReshareRequiredAt { get; set; }
    public Instant? LastReshareCompletedAt { get; set; }
}
