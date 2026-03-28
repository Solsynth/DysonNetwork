using DysonNetwork.Shared.Models;

namespace DysonNetwork.Padlock.E2EE;

public interface IE2eeModule
{
    Task<SnE2eeKeyBundle> UpsertKeyBundleAsync(Guid accountId, UpsertE2eeKeyBundleRequest request);
    Task<E2eePublicKeyBundleResponse?> GetPublicBundleAsync(Guid accountId, Guid requesterId, bool consumeOneTimePreKey);
    Task<SnE2eeSession> EnsureSessionAsync(Guid accountId, Guid peerId, EnsureE2eeSessionRequest request);
    Task<SnE2eeEnvelope> SendEnvelopeAsync(Guid senderId, SendE2eeEnvelopeRequest request);
    Task<List<SnE2eeEnvelope>> GetPendingEnvelopesAsync(Guid recipientId, int take);
    Task<SnE2eeEnvelope?> AcknowledgeEnvelopeAsync(Guid recipientId, Guid envelopeId);
    Task<SnE2eeKeyBundle> UpsertDeviceBundleAsync(
        Guid accountId,
        string deviceId,
        string? deviceLabel,
        UpsertE2eeKeyBundleRequest request
    );
    Task<List<E2eeDevicePublicBundleResponse>> GetPublicDeviceBundlesAsync(
        Guid accountId,
        Guid requesterId,
        bool consumeOneTimePreKey
    );
    Task<List<SnE2eeEnvelope>> SendFanoutEnvelopesAsync(
        Guid senderId,
        string senderDeviceId,
        SendE2eeFanoutRequest request
    );
    Task<List<SnE2eeEnvelope>> GetPendingEnvelopesByDeviceAsync(Guid recipientId, string deviceId, int take);
    Task<SnE2eeEnvelope?> AcknowledgeEnvelopeByDeviceAsync(Guid recipientId, string deviceId, Guid envelopeId);
    Task<bool> RevokeDeviceAsync(Guid accountId, string deviceId);
    Task<SnMlsKeyPackage> PublishMlsKeyPackageAsync(
        Guid accountId,
        string deviceId,
        string? deviceLabel,
        PublishMlsKeyPackageRequest request
    );
    Task<List<MlsDeviceKeyPackageResponse>> ListMlsDeviceKeyPackagesAsync(Guid accountId, Guid requesterId, bool consume);
    Task<SnMlsGroupState> BootstrapMlsGroupAsync(Guid accountId, BootstrapMlsGroupRequest request);
    Task<SnMlsGroupState?> CommitMlsGroupAsync(Guid accountId, CommitMlsGroupRequest request);
    Task<List<SnE2eeEnvelope>> FanoutMlsWelcomeAsync(Guid senderId, string senderDeviceId, FanoutMlsWelcomeRequest request);
    Task<SnMlsDeviceMembership> MarkMlsReshareRequiredAsync(Guid accountId, MarkMlsReshareRequiredRequest request);
    Task<SnMlsGroupState?> GetMlsGroupStateByGroupIdAsync(string groupId);
    Task<List<SnE2eeEnvelope>> FanoutMlsCommitAsync(Guid senderId, string senderDeviceId, FanoutMlsCommitRequest request);
    Task<List<MlsDeviceKeyPackageResponse>> GetCapableDevicesAsync(string groupId);
    Task<int> DeleteMlsGroupAsync(string groupId);
    Task NotifyGroupResetAsync(string groupId, string? reason);
    Task<SnMlsGroupState> CreateMlsGroupAsync(string groupId, long epoch, long stateVersion);
}

public interface IGroupE2eeModule : IE2eeModule
{
    Task<int> DistributeSenderKeyAsync(Guid senderId, DistributeSenderKeyRequest request);
}

public record UpsertE2eeOneTimePreKey(int KeyId, byte[] PublicKey);

public record UpsertE2eeKeyBundleRequest(
    string Algorithm,
    byte[] IdentityKey,
    int? SignedPreKeyId,
    byte[] SignedPreKey,
    byte[] SignedPreKeySignature,
    DateTimeOffset? SignedPreKeyExpiresAt,
    List<UpsertE2eeOneTimePreKey>? OneTimePreKeys,
    Dictionary<string, object>? Meta
);

public record EnsureE2eeSessionRequest(
    string? Hint,
    Dictionary<string, object>? Meta
);

public record SendE2eeEnvelopeRequest(
    Guid RecipientId,
    Guid? SessionId,
    SnE2eeEnvelopeType Type,
    string? GroupId,
    string? ClientMessageId,
    byte[] Ciphertext,
    byte[]? Header,
    byte[]? Signature,
    DateTimeOffset? ExpiresAt,
    Dictionary<string, object>? Meta
);

public record SenderKeyDistributionItem(
    Guid RecipientId,
    byte[] Ciphertext,
    byte[]? Header,
    byte[]? Signature,
    string? ClientMessageId,
    Dictionary<string, object>? Meta
);

public record DistributeSenderKeyRequest(
    string GroupId,
    List<SenderKeyDistributionItem> Items,
    DateTimeOffset? ExpiresAt
);

public record E2eePublicKeyBundleResponse(
    Guid AccountId,
    string Algorithm,
    byte[] IdentityKey,
    int? SignedPreKeyId,
    byte[] SignedPreKey,
    byte[] SignedPreKeySignature,
    DateTimeOffset? SignedPreKeyExpiresAt,
    UpsertE2eeOneTimePreKey? OneTimePreKey,
    Dictionary<string, object>? Meta
);

public record E2eeDevicePublicBundleResponse(
    Guid AccountId,
    string DeviceId,
    string? DeviceLabel,
    string Algorithm,
    byte[] IdentityKey,
    int? SignedPreKeyId,
    byte[] SignedPreKey,
    byte[] SignedPreKeySignature,
    DateTimeOffset? SignedPreKeyExpiresAt,
    UpsertE2eeOneTimePreKey? OneTimePreKey,
    Dictionary<string, object>? Meta
);

public record DeviceCiphertextEnvelope(
    string RecipientDeviceId,
    string? ClientMessageId,
    byte[] Ciphertext,
    byte[]? Header,
    byte[]? Signature,
    Dictionary<string, object>? Meta
);

public record SendE2eeFanoutRequest(
    Guid RecipientAccountId,
    Guid? SessionId,
    SnE2eeEnvelopeType Type,
    string? GroupId,
    DateTimeOffset? ExpiresAt,
    bool IncludeSenderCopy,
    List<DeviceCiphertextEnvelope> Payloads
);

public record PublishMlsKeyPackageRequest(
    byte[] KeyPackage,
    string Ciphersuite,
    Dictionary<string, object>? Meta
);

public record MlsDeviceKeyPackageResponse(
    Guid AccountId,
    string DeviceId,
    string? DeviceLabel,
    string Ciphersuite,
    byte[] KeyPackage,
    Dictionary<string, object>? Meta
);

public record BootstrapMlsGroupRequest(
    string GroupId,
    long Epoch,
    long StateVersion,
    Dictionary<string, object>? Meta
);

public record CommitMlsGroupRequest(
    string GroupId,
    long Epoch,
    string Reason,
    Dictionary<string, object>? Meta
);

public record FanoutMlsWelcomeRequest(
    string GroupId,
    Guid RecipientAccountId,
    DateTimeOffset? ExpiresAt,
    List<DeviceCiphertextEnvelope> Payloads
);

public record MarkMlsReshareRequiredRequest(
    string GroupId,
    Guid TargetAccountId,
    string TargetDeviceId,
    long Epoch,
    string Reason
);

public record FanoutMlsCommitRequest(
    string GroupId,
    long Epoch,
    List<DeviceCiphertextEnvelope> Payloads
);
