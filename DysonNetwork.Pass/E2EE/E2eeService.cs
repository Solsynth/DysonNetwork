using System.Data;
using DysonNetwork.Pass.Account;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.E2EE;

public class E2eeService(
    AppDatabase db,
    AccountService accountService,
    RemoteRingService ringService,
    ILogger<E2eeService> logger
) : IGroupE2eeModule
{
    private const string PacketType = "e2ee.envelope";

    public async Task<SnE2eeKeyBundle> UpsertKeyBundleAsync(Guid accountId, UpsertE2eeKeyBundleRequest request)
    {
        var bundle = await db.E2eeKeyBundles
            .Include(b => b.OneTimePreKeys)
            .FirstOrDefaultAsync(b => b.AccountId == accountId);
        var now = SystemClock.Instance.GetCurrentInstant();

        if (bundle is null)
        {
            bundle = new SnE2eeKeyBundle
            {
                AccountId = accountId
            };
            db.E2eeKeyBundles.Add(bundle);
        }

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

    public async Task<E2eePublicKeyBundleResponse?> GetPublicBundleAsync(Guid accountId, Guid requesterId, bool consumeOneTimePreKey)
    {
        var bundle = await db.E2eeKeyBundles
            .Include(b => b.OneTimePreKeys)
            .FirstOrDefaultAsync(b => b.AccountId == accountId);
        if (bundle is null)
            return null;

        UpsertE2eeOneTimePreKey? claimedPreKey = null;
        if (consumeOneTimePreKey)
        {
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
                claimedPreKey = new UpsertE2eeOneTimePreKey(firstAvailable.KeyId, firstAvailable.PublicKey);
                await db.SaveChangesAsync();
            }

            await tx.CommitAsync();
        }

        return new E2eePublicKeyBundleResponse(
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

    public async Task<SnE2eeSession> EnsureSessionAsync(Guid accountId, Guid peerId, EnsureE2eeSessionRequest request)
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

    public async Task<SnE2eeEnvelope> SendEnvelopeAsync(Guid senderId, SendE2eeEnvelopeRequest request)
    {
        if (request.Ciphertext.Length == 0)
            throw new InvalidOperationException("Ciphertext cannot be empty.");
        if (!string.IsNullOrWhiteSpace(request.ClientMessageId))
        {
            var existing = await db.E2eeEnvelopes.FirstOrDefaultAsync(e =>
                e.SenderId == senderId &&
                e.RecipientId == request.RecipientId &&
                e.ClientMessageId == request.ClientMessageId
            );
            if (existing is not null)
                return existing;
        }

        var recipient = await accountService.GetAccount(request.RecipientId);
        if (recipient is null)
            throw new KeyNotFoundException("Recipient not found.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var nextSequence = await db.E2eeEnvelopes
            .Where(m => m.RecipientId == request.RecipientId)
            .Select(m => (long?)m.Sequence)
            .MaxAsync() ?? 0;
        nextSequence += 1;

        var envelope = new SnE2eeEnvelope
        {
            SenderId = senderId,
            RecipientId = request.RecipientId,
            SessionId = request.SessionId,
            Type = request.Type,
            GroupId = request.GroupId,
            ClientMessageId = request.ClientMessageId,
            Sequence = nextSequence,
            Ciphertext = request.Ciphertext,
            Header = request.Header,
            Signature = request.Signature,
            ExpiresAt = request.ExpiresAt is null ? null : Instant.FromDateTimeOffset(request.ExpiresAt.Value),
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

    public async Task<List<SnE2eeEnvelope>> GetPendingEnvelopesAsync(Guid recipientId, int take)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var envelopes = await db.E2eeEnvelopes
            .Where(e => e.RecipientId == recipientId)
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
            .FirstOrDefaultAsync(e => e.Id == envelopeId && e.RecipientId == recipientId);
        if (envelope is null)
            return null;

        envelope.DeliveryStatus = SnE2eeEnvelopeStatus.Acknowledged;
        envelope.AckedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
        return envelope;
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
            await SendEnvelopeAsync(senderId, new SendE2eeEnvelopeRequest(
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
            var isConnected = await ringService.GetWebsocketConnectionStatus(
                envelope.RecipientId.ToString(),
                isUserId: true
            );
            if (!isConnected)
                return;

            await ringService.PushWebSocketPacket(
                envelope.RecipientId.ToString(),
                PacketType,
                InfraObjectCoder.ConvertObjectToByteString(new
                {
                    envelope.Id,
                    envelope.SenderId,
                    envelope.RecipientId,
                    envelope.SessionId,
                    envelope.Type,
                    envelope.GroupId,
                    envelope.ClientMessageId,
                    envelope.Sequence,
                    envelope.Ciphertext,
                    envelope.Header,
                    envelope.Signature,
                    envelope.Meta,
                    envelope.CreatedAt
                }).ToByteArray()
            );

            envelope.DeliveryStatus = SnE2eeEnvelopeStatus.Delivered;
            envelope.DeliveredAt = SystemClock.Instance.GetCurrentInstant();
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push realtime E2EE envelope {EnvelopeId} to recipient {RecipientId}",
                envelope.Id, envelope.RecipientId);
        }
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
