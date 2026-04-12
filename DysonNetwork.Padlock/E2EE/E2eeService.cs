using System.Data;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Padlock.E2EE;

public class E2EeService(
    AppDatabase db,
    RemoteWebSocketService ws,
    RemoteRingService ring,
    ILogger<E2EeService> logger
) : IE2EeModule
{
    private const string PacketType = "e2ee.envelope";
    private const string KpDepletedPacketType = "e2ee.kp.depleted";
    private const string LegacyDeviceId = "legacy-account";
    private const int MlsKeyPackageDailyLimitPerAccount = 10;
    private const int MlsKeyPackageRetentionDays = 30;
    private const int MaxFanoutPayloadsPerRequest = 1000;
    private const int MinKeyPackagesPerDevice = 3;

    public async Task<SnE2eeKeyBundle> UpsertKeyBundleAsync(Guid accountId, UpsertE2EeKeyBundleRequest request)
        => await UpsertDeviceBundleAsync(accountId, LegacyDeviceId, "Legacy account-scoped bundle", request);

    public async Task<SnE2eeKeyBundle> UpsertDeviceBundleAsync(
        Guid accountId,
        string deviceId,
        string? deviceLabel,
        UpsertE2EeKeyBundleRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new InvalidOperationException("deviceId cannot be empty.");

        var bundle = await db.E2eeKeyBundles
            .Include(b => b.OneTimePreKeys)
            .FirstOrDefaultAsync(b => b.AccountId == accountId && b.DeviceId == deviceId);
        var now = SystemClock.Instance.GetCurrentInstant();

        if (bundle is null)
        {
            bundle = new SnE2eeKeyBundle
            {
                AccountId = accountId,
                DeviceId = deviceId
            };
            db.E2eeKeyBundles.Add(bundle);
        }

        var e2EeDevice = await db.E2eeDevices.FirstOrDefaultAsync(d =>
            d.AccountId == accountId && d.DeviceId == deviceId);
        if (e2EeDevice is null)
        {
            e2EeDevice = new SnE2eeDevice
            {
                AccountId = accountId,
                DeviceId = deviceId,
                DeviceLabel = deviceLabel
            };
            db.E2eeDevices.Add(e2EeDevice);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(deviceLabel))
                e2EeDevice.DeviceLabel = deviceLabel;
            e2EeDevice.IsRevoked = false;
            e2EeDevice.RevokedAt = null;
        }
        e2EeDevice.LastBundleAt = now;

        bundle.Algorithm = request.Algorithm;
        bundle.IdentityKey = request.IdentityKey;
        bundle.SignedPreKeyId = request.SignedPreKeyId;
        bundle.SignedPreKey = request.SignedPreKey;
        bundle.SignedPreKeySignature = request.SignedPreKeySignature;
        bundle.SignedPreKeyExpiresAt = request.SignedPreKeyExpiresAt is null
            ? null
            : Instant.FromDateTimeOffset(request.SignedPreKeyExpiresAt.Value);
        bundle.Meta = request.Meta;
        bundle.UpdatedAt = now;

        if (request.OneTimePreKeys is { Count: > 0 })
        {
            var existingIds = bundle.OneTimePreKeys.Select(k => k.KeyId).ToHashSet();
            var newPreKeys = request.OneTimePreKeys
                .Where(k => !existingIds.Contains(k.KeyId))
                .Select(k => new SnE2eeOneTimePreKey
                {
                    KeyBundle = bundle,
                    AccountId = accountId,
                    DeviceId = deviceId,
                    KeyId = k.KeyId,
                    PublicKey = k.PublicKey
                })
                .ToList();

            if (newPreKeys.Count > 0)
                db.E2eeOneTimePreKeys.AddRange(newPreKeys);
        }

        await db.SaveChangesAsync();
        return bundle;
    }

    public async Task<E2EePublicKeyBundleResponse?> GetPublicBundleAsync(Guid accountId, Guid requesterId, bool consumeOneTimePreKey)
    {
        var bundle = await db.E2eeKeyBundles
            .Include(b => b.OneTimePreKeys)
            .Where(b => b.AccountId == accountId)
            .OrderByDescending(b => b.UpdatedAt)
            .FirstOrDefaultAsync();
        if (bundle is null)
            return null;

        UpsertE2EeOneTimePreKey? claimedPreKey = null;
        if (!consumeOneTimePreKey)
            return new E2EePublicKeyBundleResponse(
                bundle.AccountId,
                bundle.Algorithm,
                bundle.IdentityKey,
                bundle.SignedPreKeyId,
                bundle.SignedPreKey,
                bundle.SignedPreKeySignature,
                bundle.SignedPreKeyExpiresAt?.ToDateTimeOffset(),
                claimedPreKey,
                bundle.Meta
            );
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var firstAvailable = await db.E2eeOneTimePreKeys
            .Where(k => k.KeyBundleId == bundle.Id && !k.IsClaimed)
            .OrderBy(k => k.KeyId)
            .FirstOrDefaultAsync();

        if (firstAvailable is not null)
        {
            firstAvailable.IsClaimed = true;
            firstAvailable.ClaimedAt = SystemClock.Instance.GetCurrentInstant();
            firstAvailable.ClaimedByAccountId = requesterId;
            claimedPreKey = new UpsertE2EeOneTimePreKey(firstAvailable.KeyId, firstAvailable.PublicKey);
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();

        return new E2EePublicKeyBundleResponse(
            bundle.AccountId,
            bundle.Algorithm,
            bundle.IdentityKey,
            bundle.SignedPreKeyId,
            bundle.SignedPreKey,
            bundle.SignedPreKeySignature,
            bundle.SignedPreKeyExpiresAt?.ToDateTimeOffset(),
            claimedPreKey,
            bundle.Meta
        );
    }

    public async Task<List<E2EeDevicePublicBundleResponse>> GetPublicDeviceBundlesAsync(
        Guid accountId,
        Guid requesterId,
        bool consumeOneTimePreKey
    )
    {
        var devices = await db.E2eeDevices
            .Where(d => d.AccountId == accountId && !d.IsRevoked)
            .ToListAsync();
        if (devices.Count == 0)
            return [];

        var bundles = await db.E2eeKeyBundles
            .Where(b => b.AccountId == accountId)
            .ToListAsync();
        var bundlesByDevice = bundles.ToDictionary(b => b.DeviceId, b => b);

        var responses = new List<E2EeDevicePublicBundleResponse>();
        foreach (var device in devices)
        {
            if (!bundlesByDevice.TryGetValue(device.DeviceId, out var bundle))
                continue;

            UpsertE2EeOneTimePreKey? claimedPreKey = null;
            if (consumeOneTimePreKey)
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var firstAvailable = await db.E2eeOneTimePreKeys
                    .Where(k =>
                        k.KeyBundleId == bundle.Id &&
                        k.AccountId == accountId &&
                        k.DeviceId == device.DeviceId &&
                        !k.IsClaimed)
                    .OrderBy(k => k.KeyId)
                    .FirstOrDefaultAsync();
                if (firstAvailable is not null)
                {
                    firstAvailable.IsClaimed = true;
                    firstAvailable.ClaimedAt = SystemClock.Instance.GetCurrentInstant();
                    firstAvailable.ClaimedByAccountId = requesterId;
                    claimedPreKey = new UpsertE2EeOneTimePreKey(firstAvailable.KeyId, firstAvailable.PublicKey);
                    await db.SaveChangesAsync();
                }
                await tx.CommitAsync();
            }

            responses.Add(new E2EeDevicePublicBundleResponse(
                bundle.AccountId,
                device.DeviceId,
                device.DeviceLabel,
                bundle.Algorithm,
                bundle.IdentityKey,
                bundle.SignedPreKeyId,
                bundle.SignedPreKey,
                bundle.SignedPreKeySignature,
                bundle.SignedPreKeyExpiresAt?.ToDateTimeOffset(),
                claimedPreKey,
                bundle.Meta
            ));
        }

        return responses;
    }

    public async Task<SnMlsKeyPackage> PublishMlsKeyPackageAsync(
        Guid accountId,
        string deviceId,
        string? deviceLabel,
        PublishMlsKeyPackageRequest request
    )
    {
        if (request.KeyPackage.Length == 0)
            throw new InvalidOperationException("MLS key package cannot be empty.");

        var now = SystemClock.Instance.GetCurrentInstant();
        await PurgeExpiredMlsKeyPackagesAsync(accountId, now);
        var dayWindowStart = now - Duration.FromDays(1);
        var uploadedInDay = await db.MlsKeyPackages
            .Where(k => k.AccountId == accountId && k.CreatedAt >= dayWindowStart)
            .CountAsync();
        if (uploadedInDay >= MlsKeyPackageDailyLimitPerAccount)
            throw new InvalidOperationException(
                $"MLS key package daily upload limit exceeded. Max {MlsKeyPackageDailyLimitPerAccount} per 24h.");

        var e2EeDevice = await db.E2eeDevices.FirstOrDefaultAsync(d =>
            d.AccountId == accountId && d.DeviceId == deviceId);
        if (e2EeDevice is null)
        {
            e2EeDevice = new SnE2eeDevice
            {
                AccountId = accountId,
                DeviceId = deviceId,
                DeviceLabel = deviceLabel
            };
            db.E2eeDevices.Add(e2EeDevice);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(deviceLabel))
                e2EeDevice.DeviceLabel = deviceLabel;
            e2EeDevice.IsRevoked = false;
            e2EeDevice.RevokedAt = null;
        }
        e2EeDevice.LastBundleAt = now;

        var keyPackage = new SnMlsKeyPackage
        {
            AccountId = accountId,
            DeviceId = deviceId,
            DeviceLabel = deviceLabel,
            KeyPackage = request.KeyPackage,
            Ciphersuite = request.Ciphersuite,
            Meta = request.Meta,
            IsConsumed = false
        };
        db.MlsKeyPackages.Add(keyPackage);
        await db.SaveChangesAsync();
        return keyPackage;
    }

    public async Task<List<MlsDeviceKeyPackageResponse>> ListMlsDeviceKeyPackagesAsync(
        Guid accountId,
        Guid? requesterId,
        bool consume
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await PurgeExpiredMlsKeyPackagesAsync(accountId, now);
        var activeDevices = await db.E2eeDevices
            .Where(d => d.AccountId == accountId && !d.IsRevoked)
            .ToListAsync();
        var responses = new List<MlsDeviceKeyPackageResponse>();
        var dirty = false;
        string? consumedDeviceId = null;
        string? consumedDeviceLabel = null;

        foreach (var device in activeDevices)
        {
            var package = await db.MlsKeyPackages
                .Where(k => k.AccountId == accountId && k.DeviceId == device.DeviceId && !k.IsConsumed)
                .OrderBy(k => k.CreatedAt)
                .FirstOrDefaultAsync() ?? await db.MlsKeyPackages
                .Where(k => k.AccountId == accountId && k.DeviceId == device.DeviceId)
                .OrderByDescending(k => k.CreatedAt)
                .FirstOrDefaultAsync();
            if (package is null) continue;

            if (consume && !package.IsConsumed)
            {
                package.IsConsumed = true;
                package.ConsumedAt = now;
                package.ConsumedByAccountId = requesterId;
                dirty = true;
                consumedDeviceId = device.DeviceId;
                consumedDeviceLabel = device.DeviceLabel;
            }

            responses.Add(new MlsDeviceKeyPackageResponse(
                package.AccountId,
                package.DeviceId,
                device.DeviceLabel ?? package.DeviceLabel,
                package.Ciphersuite,
                package.KeyPackage,
                package.Meta
            ));
        }

        if (!dirty) return responses;
        await db.SaveChangesAsync();
        if (consumedDeviceId is not null)
        {
            await CheckAndNotifyKpDepletedAsync(accountId, consumedDeviceId, consumedDeviceLabel);
        }

        return responses;
    }

    public async Task<MlsKeyPackageStatusResponse> GetMlsKeyPackageStatusAsync(Guid accountId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await PurgeExpiredMlsKeyPackagesAsync(accountId, now);
        
        var activeDevices = await db.E2eeDevices
            .Where(d => d.AccountId == accountId && !d.IsRevoked)
            .ToListAsync();
        
        var devicesNeedingKps = new List<MlsDeviceKpStatus>();
        
        foreach (var device in activeDevices)
        {
            var availableCount = await db.MlsKeyPackages
                .Where(k => k.AccountId == accountId && k.DeviceId == device.DeviceId && !k.IsConsumed)
                .CountAsync();
            
            if (availableCount < MinKeyPackagesPerDevice)
            {
                devicesNeedingKps.Add(new MlsDeviceKpStatus(
                    device.DeviceId,
                    device.DeviceLabel,
                    availableCount
                ));
            }
        }
        
        return new MlsKeyPackageStatusResponse(
            NeedsMoreKps: devicesNeedingKps.Count > 0,
            DevicesNeedingKps: devicesNeedingKps
        );
    }

    private async Task CheckAndNotifyKpDepletedAsync(Guid accountId, string deviceId, string? deviceLabel)
    {
        var availableCount = await db.MlsKeyPackages
            .Where(k => k.AccountId == accountId && k.DeviceId == deviceId && !k.IsConsumed)
            .CountAsync();
        
        if (availableCount < MinKeyPackagesPerDevice)
        {
            await NotifyKpDepletedAsync(accountId, deviceId, deviceLabel, availableCount);
        }
    }

    private async Task NotifyKpDepletedAsync(Guid accountId, string mlsDeviceId, string? deviceLabel, int availableCount)
    {
        try
        {
            var payload = InfraObjectCoder.ConvertObjectToByteString(new
            {
                mlsDeviceId,
                deviceId = mlsDeviceId,
                deviceLabel,
                availableCount
            }).ToByteArray();
            
            await ring.SendWebSocketPacketToUser(accountId.ToString(), KpDepletedPacketType, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send KP depleted notification for account {AccountId}, device {DeviceId}",
                accountId, mlsDeviceId);
        }
    }

    public async Task<SnMlsGroupState> BootstrapMlsGroupAsync(Guid accountId, BootstrapMlsGroupRequest request)
    {
        var existing = await db.MlsGroupStates
            .FirstOrDefaultAsync(s => s.MlsGroupId == request.GroupId);
        if (existing is not null)
        {
            existing.Epoch = request.Epoch;
            existing.StateVersion = request.StateVersion;
            existing.Meta = request.Meta;
            existing.LastCommitAt = SystemClock.Instance.GetCurrentInstant();
            await db.SaveChangesAsync();
            return existing;
        }

        var state = new SnMlsGroupState
        {
            MlsGroupId = request.GroupId,
            Epoch = request.Epoch,
            StateVersion = request.StateVersion,
            LastCommitAt = SystemClock.Instance.GetCurrentInstant(),
            Meta = request.Meta
        };
        db.MlsGroupStates.Add(state);
        await db.SaveChangesAsync();
        return state;
    }

    public async Task<SnMlsGroupState?> CommitMlsGroupAsync(Guid accountId, CommitMlsGroupRequest request)
    {
        var state = await db.MlsGroupStates
            .FirstOrDefaultAsync(s => s.MlsGroupId == request.GroupId);
        if (state is null)
            return null;

        state.Epoch = Math.Max(state.Epoch, request.Epoch);
        state.StateVersion += 1;
        state.LastCommitAt = SystemClock.Instance.GetCurrentInstant();
        state.Meta = request.Meta is null
            ? state.Meta
            : new Dictionary<string, object>(request.Meta)
            {
                ["reason"] = request.Reason
            };
        await db.SaveChangesAsync();
        return state;
    }

    public async Task<List<SnE2eeEnvelope>> FanoutMlsWelcomeAsync(
        Guid senderId,
        string senderDeviceId,
        FanoutMlsWelcomeRequest request
    )
    {
        if (request.RecipientAccountId.HasValue)
        {
            var payloads = request.Payloads
                .Select(p => new DeviceCiphertextEnvelope(
                    p.RecipientDeviceId,
                    p.ClientMessageId,
                    p.Ciphertext,
                    p.Header,
                    p.Signature,
                    p.Meta is null
                        ? new Dictionary<string, object> { ["mls_group_id"] = request.GroupId }
                        : new Dictionary<string, object>(p.Meta) { ["mls_group_id"] = request.GroupId }
                ))
                .ToList();

            return await SendFanoutEnvelopesAsync(senderId, senderDeviceId, new SendE2EeFanoutRequest(
                request.RecipientAccountId.Value,
                null,
                SnE2eeEnvelopeType.MlsWelcome,
                request.GroupId,
                request.ExpiresAt,
                IncludeSenderCopy: false,
                payloads
            ));
        }

        if (request.Payloads.Count == 0)
            throw new InvalidOperationException("No payloads provided for all-fanout welcome.");

        var firstPayload = request.Payloads.First();
        return await FanoutMlsMessageToGroupAsync(senderId, senderDeviceId, new FanoutMlsGroupMessageRequest(
            request.GroupId,
            firstPayload.Ciphertext,
            firstPayload.Header,
            firstPayload.Signature,
            firstPayload.ClientMessageId,
            firstPayload.Meta is null
                ? new Dictionary<string, object> { ["mls_group_id"] = request.GroupId }
                : new Dictionary<string, object>(firstPayload.Meta) { ["mls_group_id"] = request.GroupId }
        ), SnE2eeEnvelopeType.MlsWelcome);
    }

    public async Task<SnMlsDeviceMembership> MarkMlsReshareRequiredAsync(
        Guid accountId,
        MarkMlsReshareRequiredRequest request
    )
    {
        var membership = await db.MlsDeviceMemberships
            .FirstOrDefaultAsync(m =>
                m.MlsGroupId == request.GroupId &&
                m.AccountId == request.TargetAccountId &&
                m.DeviceId == request.TargetDeviceId);
        if (membership is null)
        {
            membership = new SnMlsDeviceMembership
            {
                MlsGroupId = request.GroupId,
                AccountId = request.TargetAccountId,
                DeviceId = request.TargetDeviceId,
                JoinedEpoch = request.Epoch,
                LastSeenEpoch = request.Epoch
            };
            db.MlsDeviceMemberships.Add(membership);
        }

        membership.MlsGroupId = request.GroupId;
        membership.LastReshareRequiredAt = SystemClock.Instance.GetCurrentInstant();
        membership.LastSeenEpoch = request.Epoch;
        await db.SaveChangesAsync();
        return membership;
    }

    public async Task<SnMlsDeviceMembership> AddMlsDeviceMembershipAsync(Guid accountId, string deviceId, string groupId, long epoch)
    {
        var membership = await db.MlsDeviceMemberships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m =>
                m.MlsGroupId == groupId &&
                m.AccountId == accountId &&
                m.DeviceId == deviceId);

        if (membership is null)
        {
            membership = new SnMlsDeviceMembership
            {
                MlsGroupId = groupId,
                AccountId = accountId,
                DeviceId = deviceId,
                JoinedEpoch = epoch,
                LastSeenEpoch = epoch
            };
            db.MlsDeviceMemberships.Add(membership);
        }
        else
        {
            membership.DeletedAt = null;
            membership.LastSeenEpoch = epoch;
        }

        membership.LastReshareRequiredAt = null;
        membership.LastReshareCompletedAt = null;

        await db.SaveChangesAsync();
        return membership;
    }

    public async Task<List<SnMlsDeviceMembership>> GetMlsDevicesNeedingReshareAsync(string groupId)
    {
        return await db.MlsDeviceMemberships
            .Where(m => m.MlsGroupId == groupId)
            .Where(m => m.LastReshareRequiredAt != null && m.LastReshareCompletedAt == null)
            .ToListAsync();
    }

    public async Task<List<SnMlsDeviceMembership>> GetDeviceMlsReshareStatusAsync(Guid accountId, string deviceId)
    {
        return await db.MlsDeviceMemberships
            .Where(m => m.AccountId == accountId && m.DeviceId == deviceId)
            .Where(m => m.LastReshareRequiredAt != null && m.LastReshareCompletedAt == null)
            .ToListAsync();
    }

    public async Task<bool> CompleteMlsReshareAsync(Guid accountId, string deviceId, string groupId)
    {
        var membership = await db.MlsDeviceMemberships
            .FirstOrDefaultAsync(m =>
                m.AccountId == accountId &&
                m.DeviceId == deviceId &&
                m.MlsGroupId == groupId);

        if (membership is null) return false;

        membership.LastReshareCompletedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<int> MarkAllDevicesReshareRequiredAsync(string groupId, string reason)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var memberships = await db.MlsDeviceMemberships
            .Where(m => m.MlsGroupId == groupId)
            .ToListAsync();

        foreach (var membership in memberships)
        {
            membership.LastReshareRequiredAt = now;
            membership.LastReshareCompletedAt = null;
        }

        await db.SaveChangesAsync();
        return memberships.Count;
    }

    public async Task<SnMlsGroupState?> GetMlsGroupStateByGroupIdAsync(string groupId)
    {
        return await db.MlsGroupStates
            .FirstOrDefaultAsync(s => s.MlsGroupId == groupId);
    }

    public async Task<List<SnE2eeEnvelope>> FanoutMlsCommitAsync(
        Guid senderId,
        string senderDeviceId,
        FanoutMlsCommitRequest request
    )
    {
        var memberships = await db.MlsDeviceMemberships
            .Where(m => m.MlsGroupId == request.GroupId)
            .ToListAsync();

        if (memberships.Count == 0)
            throw new InvalidOperationException("No devices found in group.");

        var envelopes = new List<SnE2eeEnvelope>();
        var groupedByAccount = memberships.GroupBy(m => m.AccountId);

        foreach (var accountGroup in groupedByAccount)
        {
            var accountId = accountGroup.Key;
            var accountPayloads = accountGroup
                .Select(m => new DeviceCiphertextEnvelope(
                    m.DeviceId,
                    request.ClientMessageId,
                    request.Ciphertext,
                    request.Header,
                    request.Signature,
                    request.Meta is null
                        ? new Dictionary<string, object> { ["mls_group_id"] = request.GroupId }
                        : new Dictionary<string, object>(request.Meta) { ["mls_group_id"] = request.GroupId }
                ))
                .ToList();

            if (accountPayloads.Count == 0) continue;

            var result = await SendFanoutEnvelopesAsync(senderId, senderDeviceId, new SendE2EeFanoutRequest(
                accountId,
                null,
                SnE2eeEnvelopeType.MlsCommit,
                request.GroupId,
                null,
                IncludeSenderCopy: false,
                accountPayloads
            ));
            envelopes.AddRange(result);
        }

        return envelopes;
    }

    public async Task<List<SnE2eeEnvelope>> FanoutMlsMessageToGroupAsync(
        Guid senderId,
        string senderDeviceId,
        FanoutMlsGroupMessageRequest request,
        SnE2eeEnvelopeType envelopeType = SnE2eeEnvelopeType.MlsApplication
    )
    {
        var memberships = await db.MlsDeviceMemberships
            .Where(m => m.MlsGroupId == request.GroupId)
            .ToListAsync();

        if (memberships.Count == 0)
            throw new InvalidOperationException("No devices found in group.");

        var envelopes = new List<SnE2eeEnvelope>();
        var groupedByAccount = memberships.GroupBy(m => m.AccountId);

        foreach (var accountGroup in groupedByAccount)
        {
            var accountId = accountGroup.Key;
            var accountPayloads = accountGroup
                .Select(m => new DeviceCiphertextEnvelope(
                    m.DeviceId,
                    request.ClientMessageId,
                    request.Ciphertext,
                    request.Header,
                    request.Signature,
                    request.Meta is null
                        ? new Dictionary<string, object> { ["mls_group_id"] = request.GroupId }
                        : new Dictionary<string, object>(request.Meta) { ["mls_group_id"] = request.GroupId }
                ))
                .ToList();

            if (accountPayloads.Count == 0) continue;

            var result = await SendFanoutEnvelopesAsync(senderId, senderDeviceId, new SendE2EeFanoutRequest(
                accountId,
                null,
                envelopeType,
                request.GroupId,
                null,
                IncludeSenderCopy: false,
                accountPayloads
            ));
            envelopes.AddRange(result);
        }

        return envelopes;
    }

    public async Task<List<MlsDeviceKeyPackageResponse>> GetCapableDevicesAsync(string groupId)
    {
        var memberships = await db.MlsDeviceMemberships
            .Where(m => m.MlsGroupId == groupId)
            .ToListAsync();

        var responses = new List<MlsDeviceKeyPackageResponse>();
        foreach (var membership in memberships)
        {
            var package = await db.MlsKeyPackages
                .Where(k => k.AccountId == membership.AccountId && k.DeviceId == membership.DeviceId && !k.IsConsumed)
                .OrderBy(k => k.CreatedAt)
                .FirstOrDefaultAsync();

            if (package is not null)
            {
                responses.Add(new MlsDeviceKeyPackageResponse(
                    package.AccountId,
                    package.DeviceId,
                    package.DeviceLabel,
                    package.Ciphersuite,
                    package.KeyPackage,
                    package.Meta
                ));
            }
        }

        return responses;
    }

    public async Task<int> DeleteMlsGroupAsync(string groupId)
    {
        var states = await db.MlsGroupStates
            .Where(s => s.MlsGroupId == groupId)
            .ToListAsync();

        if (states.Count == 0) return 0;

        db.MlsGroupStates.RemoveRange(states);

        var memberships = await db.MlsDeviceMemberships
            .Where(m => m.MlsGroupId == groupId)
            .ToListAsync();

        db.MlsDeviceMemberships.RemoveRange(memberships);

        await db.SaveChangesAsync();
        return states.Count;
    }

    public async Task NotifyGroupResetAsync(string groupId, string? reason)
    {
        var memberships = await db.MlsDeviceMemberships
            .Where(m => m.MlsGroupId == groupId)
            .Select(m => m.AccountId)
            .Distinct()
            .ToListAsync();
        if (memberships is { Count: 0 }) return;

        var userIds = memberships.Select(m => m.ToString()).ToList();

        var payload = InfraObjectCoder.ConvertObjectToByteString(new
        {
            Type = "mls.group.reset",
            GroupId = groupId,
            Reason = reason,
            Timestamp = SystemClock.Instance.GetCurrentInstant().ToString()
        });

        await ws.PushWebSocketPacketToUsers(userIds, "e2ee.group.reset", payload.ToByteArray());
    }

    public async Task<SnMlsGroupState> CreateMlsGroupAsync(
        string groupId,
        long epoch,
        long stateVersion
    )
    {
        var state = new SnMlsGroupState
        {
            MlsGroupId = groupId,
            Epoch = epoch,
            StateVersion = stateVersion,
            LastCommitAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.MlsGroupStates.Add(state);
        await db.SaveChangesAsync();
        return state;
    }

    public async Task<UploadGroupInfoResponse> UploadGroupInfoAsync(string groupId, byte[] groupInfo, byte[] ratchetTree)
    {
        var state = await db.MlsGroupStates
            .FirstOrDefaultAsync(s => s.MlsGroupId == groupId);

        if (state is null)
        {
            state = new SnMlsGroupState
            {
                MlsGroupId = groupId,
                Epoch = 0,
                StateVersion = 0,
                GroupInfo = groupInfo,
                RatchetTree = ratchetTree,
                LastCommitAt = SystemClock.Instance.GetCurrentInstant()
            };
            db.MlsGroupStates.Add(state);
        }
        else
        {
            state.GroupInfo = groupInfo;
            state.RatchetTree = ratchetTree;
        }

        await db.SaveChangesAsync();
        return new UploadGroupInfoResponse(true, state.MlsGroupId, state.Epoch);
    }

    public async Task<SnE2eeSession> EnsureSessionAsync(Guid accountId, Guid peerId, EnsureE2EeSessionRequest request)
    {
        EnsurePairOrder(accountId, peerId, out var accountAId, out var accountBId);

        var session = await db.E2eeSessions
            .FirstOrDefaultAsync(s => s.AccountAId == accountAId && s.AccountBId == accountBId);
        if (session is not null)
        {
            session.Hint = request.Hint ?? session.Hint;
            session.Meta = request.Meta ?? session.Meta;
            await db.SaveChangesAsync();
            return session;
        }

        session = new SnE2eeSession
        {
            AccountAId = accountAId,
            AccountBId = accountBId,
            InitiatedById = accountId,
            Hint = request.Hint,
            Meta = request.Meta
        };
        db.E2eeSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    public async Task<SnE2eeEnvelope> SendEnvelopeAsync(Guid senderId, SendE2EeEnvelopeRequest request)
    {
        if (request.Ciphertext.Length == 0)
            throw new InvalidOperationException("Ciphertext cannot be empty.");
        if (!string.IsNullOrWhiteSpace(request.ClientMessageId))
        {
            var existing = await db.E2eeEnvelopes.FirstOrDefaultAsync(e =>
                e.SenderId == senderId &&
                e.RecipientAccountId == request.RecipientId &&
                e.RecipientDeviceId == null &&
                e.ClientMessageId == request.ClientMessageId
            );
            if (existing is not null)
                return existing;
        }

        var recipientExists = await db.Accounts.AnyAsync(a => a.Id == request.RecipientId);
        if (!recipientExists)
            throw new KeyNotFoundException("Recipient not found.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var nextSequence = await db.E2eeEnvelopes
            .Where(m => m.RecipientAccountId == request.RecipientId && m.RecipientDeviceId == null)
            .Select(m => (long?)m.Sequence)
            .MaxAsync() ?? 0;
        nextSequence += 1;

        var envelope = new SnE2eeEnvelope
        {
            SenderId = senderId,
            SenderDeviceId = LegacyDeviceId,
            RecipientId = request.RecipientId,
            RecipientAccountId = request.RecipientId,
            RecipientDeviceId = null,
            SessionId = request.SessionId,
            Type = request.Type,
            GroupId = request.GroupId,
            ClientMessageId = request.ClientMessageId,
            Sequence = nextSequence,
            Ciphertext = request.Ciphertext,
            Header = request.Header,
            Signature = request.Signature,
            ExpiresAt = request.ExpiresAt is null ? null : Instant.FromDateTimeOffset(request.ExpiresAt.Value),
            LegacyAccountScoped = true,
            Meta = request.Meta,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.E2eeEnvelopes.Add(envelope);

        if (request.SessionId.HasValue)
        {
            var session = await db.E2eeSessions.FirstOrDefaultAsync(s => s.Id == request.SessionId.Value);
            if (session is not null)
                session.LastMessageAt = now;
        }

        await db.SaveChangesAsync();

        await TryDeliverEnvelopeAsync(envelope);
        return envelope;
    }

    public async Task<List<SnE2eeEnvelope>> SendFanoutEnvelopesAsync(
        Guid senderId,
        string senderDeviceId,
        SendE2EeFanoutRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(senderDeviceId))
            throw new InvalidOperationException("senderDeviceId cannot be empty.");
        if (request.Payloads.Count == 0)
            throw new InvalidOperationException("payloads cannot be empty.");
        if (request.Payloads.Count > MaxFanoutPayloadsPerRequest)
            throw new InvalidOperationException(
                $"Too many payloads in one fanout request. Max allowed: {MaxFanoutPayloadsPerRequest}.");

        var recipientExists = await db.Accounts.AnyAsync(a => a.Id == request.RecipientAccountId);
        if (!recipientExists)
            throw new KeyNotFoundException("Recipient not found.");

        var activeDevices = await db.E2eeDevices
            .Where(d => d.AccountId == request.RecipientAccountId && !d.IsRevoked)
            .Select(d => d.DeviceId)
            .ToListAsync();
        if (activeDevices.Count == 0)
            throw new InvalidOperationException("Recipient has no active E2EE devices.");

        var isMlsType = request.Type is SnE2eeEnvelopeType.MlsWelcome
            or SnE2eeEnvelopeType.MlsCommit
            or SnE2eeEnvelopeType.MlsApplication
            or SnE2eeEnvelopeType.MlsProposal
            or SnE2eeEnvelopeType.Control;

        if (!isMlsType)
        {
            var payloadByDevice = request.Payloads.ToDictionary(p => p.RecipientDeviceId, p => p);
            var missingDevices = activeDevices.Where(d => !payloadByDevice.ContainsKey(d)).ToList();
            if (missingDevices.Count > 0)
                throw new InvalidOperationException($"Missing ciphertext for recipient devices: {string.Join(", ", missingDevices)}");

            var extraDevices = request.Payloads.Select(p => p.RecipientDeviceId).Where(d => !activeDevices.Contains(d)).Distinct()
                .ToList();
            if (extraDevices.Count > 0)
                throw new InvalidOperationException($"Payload includes unknown/revoked devices: {string.Join(", ", extraDevices)}");
        }
        else
        {
            var payloadDeviceIds = request.Payloads.Select(p => p.RecipientDeviceId).ToList();
            var unknownDevices = payloadDeviceIds.Where(d => !activeDevices.Contains(d)).Distinct().ToList();
            if (unknownDevices.Count > 0)
                throw new InvalidOperationException($"Payload includes unknown/revoked devices: {string.Join(", ", unknownDevices)}");
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var envelopes = new List<SnE2eeEnvelope>();

        foreach (var payload in request.Payloads)
        {
            var envelope = await CreateEnvelopeForTargetAsync(
                senderId,
                senderDeviceId,
                request.RecipientAccountId,
                payload.RecipientDeviceId,
                request.SessionId,
                request.Type,
                request.GroupId,
                payload.ClientMessageId,
                payload.Ciphertext,
                payload.Header,
                payload.Signature,
                request.ExpiresAt,
                payload.Meta,
                legacyAccountScoped: false,
                createdAt: now
            );
            envelopes.Add(envelope);
        }

        if (request.IncludeSenderCopy && request.RecipientAccountId != senderId)
        {
            var senderPayload = request.Payloads.FirstOrDefault(p => p.RecipientDeviceId == senderDeviceId);
            if (senderPayload is not null)
            {
                var senderCopyMeta = senderPayload.Meta is null
                    ? []
                    : new Dictionary<string, object>(senderPayload.Meta);
                senderCopyMeta["sender_copy"] = true;
                var senderCopy = await CreateEnvelopeForTargetAsync(
                    senderId,
                    senderDeviceId,
                    senderId,
                    senderDeviceId,
                    request.SessionId,
                    request.Type,
                    request.GroupId,
                    senderPayload.ClientMessageId is null ? null : $"{senderPayload.ClientMessageId}:self",
                    senderPayload.Ciphertext,
                    senderPayload.Header,
                    senderPayload.Signature,
                    request.ExpiresAt,
                    senderCopyMeta,
                    legacyAccountScoped: false,
                    createdAt: now
                );
                envelopes.Add(senderCopy);
            }
        }

        if (request.SessionId.HasValue)
        {
            var session = await db.E2eeSessions.FirstOrDefaultAsync(s => s.Id == request.SessionId.Value);
            session?.LastMessageAt = now;
        }

        await db.SaveChangesAsync();
        foreach (var envelope in envelopes)
            await TryDeliverEnvelopeAsync(envelope);

        return envelopes;
    }

    public async Task<List<SnE2eeEnvelope>> GetPendingEnvelopesAsync(Guid recipientId, int take)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var envelopes = await db.E2eeEnvelopes
            .Where(e => e.RecipientAccountId == recipientId && e.RecipientDeviceId == null)
            .Where(e => e.DeliveryStatus != SnE2eeEnvelopeStatus.Acknowledged)
            .Where(e => e.ExpiresAt == null || e.ExpiresAt > now)
            .OrderBy(e => e.Sequence)
            .Take(take)
            .ToListAsync();

        var dirty = false;
        foreach (var envelope in envelopes.Where(e => e.DeliveryStatus == SnE2eeEnvelopeStatus.Pending))
        {
            envelope.DeliveryStatus = SnE2eeEnvelopeStatus.Delivered;
            envelope.DeliveredAt = now;
            dirty = true;
        }

        if (dirty)
            await db.SaveChangesAsync();

        return envelopes;
    }

    public async Task<List<SnE2eeEnvelope>> GetPendingEnvelopesByDeviceAsync(Guid recipientId, string deviceId, int take)
    {
        var activeDevice = await db.E2eeDevices
            .Where(d => d.AccountId == recipientId && d.DeviceId == deviceId && !d.IsRevoked)
            .FirstOrDefaultAsync();
        if (activeDevice is null)
            return [];

        var now = SystemClock.Instance.GetCurrentInstant();
        var envelopes = await db.E2eeEnvelopes
            .Where(e => e.RecipientAccountId == recipientId && e.RecipientDeviceId == deviceId)
            .Where(e => e.DeliveryStatus != SnE2eeEnvelopeStatus.Acknowledged)
            .Where(e => e.ExpiresAt == null || e.ExpiresAt > now)
            .OrderBy(e => e.Sequence)
            .Take(take)
            .ToListAsync();

        var dirty = false;
        foreach (var envelope in envelopes.Where(e => e.DeliveryStatus == SnE2eeEnvelopeStatus.Pending))
        {
            envelope.DeliveryStatus = SnE2eeEnvelopeStatus.Delivered;
            envelope.DeliveredAt = now;
            dirty = true;
        }

        if (dirty)
            await db.SaveChangesAsync();

        return envelopes;
    }

    public async Task<SnE2eeEnvelope?> AcknowledgeEnvelopeAsync(Guid recipientId, Guid envelopeId)
    {
        var envelope = await db.E2eeEnvelopes
            .FirstOrDefaultAsync(e => e.Id == envelopeId && e.RecipientAccountId == recipientId && e.RecipientDeviceId == null);
        if (envelope is null)
            return null;

        envelope.DeliveryStatus = SnE2eeEnvelopeStatus.Acknowledged;
        envelope.AckedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
        return envelope;
    }

    public async Task<SnE2eeEnvelope?> AcknowledgeEnvelopeByDeviceAsync(Guid recipientId, string deviceId, Guid envelopeId)
    {
        var activeDevice = await db.E2eeDevices
            .Where(d => d.AccountId == recipientId && d.DeviceId == deviceId && !d.IsRevoked)
            .FirstOrDefaultAsync();
        if (activeDevice is null)
            return null;

        var envelope = await db.E2eeEnvelopes
            .FirstOrDefaultAsync(e => e.Id == envelopeId && e.RecipientAccountId == recipientId && e.RecipientDeviceId == deviceId);
        if (envelope is null)
            return null;

        envelope.DeliveryStatus = SnE2eeEnvelopeStatus.Acknowledged;
        envelope.AckedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
        return envelope;
    }

    public async Task<bool> RevokeDeviceAsync(Guid accountId, string deviceId)
    {
        var device = await db.E2eeDevices
            .FirstOrDefaultAsync(d => d.AccountId == accountId && d.DeviceId == deviceId);
        if (device is null)
            return false;

        if (device.IsRevoked)
            return true;

        device.IsRevoked = true;
        var now = SystemClock.Instance.GetCurrentInstant();
        device.RevokedAt = now;

        var pending = await db.E2eeEnvelopes
            .Where(e =>
                e.RecipientAccountId == accountId &&
                e.RecipientDeviceId == deviceId &&
                e.DeliveryStatus != SnE2eeEnvelopeStatus.Acknowledged)
            .ToListAsync();
        var purgedCount = pending.Count;
        if (purgedCount > 0)
            db.RemoveRange(pending);

        var siblingDevices = await db.E2eeDevices
            .Where(d => d.AccountId == accountId && !d.IsRevoked && d.DeviceId != deviceId)
            .Select(d => d.DeviceId)
            .ToListAsync();
        var controlEnvelopes = new List<SnE2eeEnvelope>();
        foreach (var targetDeviceId in siblingDevices)
        {
            var controlEnvelope = await CreateEnvelopeForTargetAsync(
                accountId,
                LegacyDeviceId,
                accountId,
                targetDeviceId,
                null,
                SnE2eeEnvelopeType.Control,
                null,
                $"mls-revoke-{deviceId}-{now.ToUnixTimeMilliseconds()}-{targetDeviceId}",
                [1],
                null,
                null,
                null,
                new Dictionary<string, object>
                {
                    ["event"] = "mls_device_revoked",
                    ["revoked_device_id"] = deviceId
                },
                legacyAccountScoped: false,
                createdAt: now
            );
            controlEnvelopes.Add(controlEnvelope);
        }

        await db.SaveChangesAsync();
        foreach (var envelope in controlEnvelopes)
            await TryDeliverEnvelopeAsync(envelope);
        logger.LogInformation(
            "Revoked device {DeviceId} for account {AccountId}. Purged pending envelopes: {PurgedCount}",
            deviceId, accountId, purgedCount);
        return true;
    }

    private async Task PurgeExpiredMlsKeyPackagesAsync(Guid accountId, Instant now)
    {
        var cutoff = now - Duration.FromDays(MlsKeyPackageRetentionDays);
        var expired = await db.MlsKeyPackages
            .Where(k => k.AccountId == accountId && k.CreatedAt < cutoff)
            .ToListAsync();
        if (expired.Count == 0)
            return;

        db.RemoveRange(expired);
        await db.SaveChangesAsync();
    }

    public async Task<int> DistributeSenderKeyAsync(Guid senderId, DistributeSenderKeyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GroupId))
            throw new InvalidOperationException("GroupId cannot be empty.");
        if (request.Items.Count == 0)
            return 0;

        var sent = 0;
        foreach (var item in request.Items)
        {
            await SendEnvelopeAsync(senderId, new SendE2EeEnvelopeRequest(
                item.RecipientId,
                null,
                SnE2eeEnvelopeType.SenderKeyDistribution,
                request.GroupId,
                item.ClientMessageId,
                item.Ciphertext,
                item.Header,
                item.Signature,
                request.ExpiresAt,
                item.Meta
            ));
            sent++;
        }

        return sent;
    }

    private async Task TryDeliverEnvelopeAsync(SnE2eeEnvelope envelope)
    {
        try
        {
            var targetDeviceId = envelope.RecipientDeviceId;
            var isConnected = targetDeviceId is null
                ? await ws.GetWebsocketConnectionStatus(envelope.RecipientAccountId.ToString(), isUserId: true)
                : await ws.GetWebsocketConnectionStatus(targetDeviceId, isUserId: false);
            if (!isConnected)
                return;

            var payload = InfraObjectCoder.ConvertObjectToByteString(new
            {
                envelope.Id,
                envelope.SenderId,
                envelope.SenderDeviceId,
                envelope.RecipientId,
                envelope.RecipientAccountId,
                envelope.RecipientDeviceId,
                envelope.SessionId,
                envelope.Type,
                envelope.GroupId,
                envelope.ClientMessageId,
                envelope.Sequence,
                envelope.Ciphertext,
                envelope.Header,
                envelope.Signature,
                envelope.Meta,
                envelope.LegacyAccountScoped,
                envelope.CreatedAt
            });
            if (targetDeviceId is null)
                await ws.PushWebSocketPacket(envelope.RecipientAccountId.ToString(), PacketType, payload.ToByteArray());
            else
                await ws.PushWebSocketPacketToDevice(targetDeviceId, PacketType, payload.ToByteArray());

            envelope.DeliveryStatus = SnE2eeEnvelopeStatus.Delivered;
            envelope.DeliveredAt = SystemClock.Instance.GetCurrentInstant();
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to push realtime E2EE envelope {EnvelopeId} to recipient {RecipientId}/{RecipientDeviceId}",
                envelope.Id, envelope.RecipientAccountId, envelope.RecipientDeviceId);
        }
    }

    private async Task<SnE2eeEnvelope> CreateEnvelopeForTargetAsync(
        Guid senderId,
        string senderDeviceId,
        Guid recipientAccountId,
        string recipientDeviceId,
        Guid? sessionId,
        SnE2eeEnvelopeType type,
        string? groupId,
        string? clientMessageId,
        byte[] ciphertext,
        byte[]? header,
        byte[]? signature,
        DateTimeOffset? expiresAt,
        Dictionary<string, object>? meta,
        bool legacyAccountScoped,
        Instant createdAt
    )
    {
        if (ciphertext.Length == 0)
            throw new InvalidOperationException("Ciphertext cannot be empty.");

        if (!string.IsNullOrWhiteSpace(clientMessageId))
        {
            var existing = await db.E2eeEnvelopes.FirstOrDefaultAsync(e =>
                e.SenderId == senderId &&
                e.SenderDeviceId == senderDeviceId &&
                e.RecipientAccountId == recipientAccountId &&
                e.RecipientDeviceId == recipientDeviceId &&
                e.ClientMessageId == clientMessageId
            );
            if (existing is not null)
                return existing;
        }

        var nextSequence = await db.E2eeEnvelopes
            .Where(m => m.RecipientAccountId == recipientAccountId && m.RecipientDeviceId == recipientDeviceId)
            .Select(m => (long?)m.Sequence)
            .MaxAsync() ?? 0;
        nextSequence += 1;

        var envelope = new SnE2eeEnvelope
        {
            SenderId = senderId,
            SenderDeviceId = senderDeviceId,
            RecipientId = recipientAccountId,
            RecipientAccountId = recipientAccountId,
            RecipientDeviceId = recipientDeviceId,
            SessionId = sessionId,
            Type = type,
            GroupId = groupId,
            ClientMessageId = clientMessageId,
            Sequence = nextSequence,
            Ciphertext = ciphertext,
            Header = header,
            Signature = signature,
            ExpiresAt = expiresAt is null ? null : Instant.FromDateTimeOffset(expiresAt.Value),
            Meta = meta,
            LegacyAccountScoped = legacyAccountScoped,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
        db.E2eeEnvelopes.Add(envelope);
        return envelope;
    }

    private static void EnsurePairOrder(Guid left, Guid right, out Guid accountAId, out Guid accountBId)
    {
        if (left.CompareTo(right) <= 0)
        {
            accountAId = left;
            accountBId = right;
        }
        else
        {
            accountAId = right;
            accountBId = left;
        }
    }
}
