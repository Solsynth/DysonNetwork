using System.Security.Cryptography;
using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Nfc;

public class NfcResolveResult
{
    public SnAccount User { get; set; } = null!;
    public SnAccountProfile? Profile { get; set; }
    public bool IsFriend { get; set; }
    public bool IsClaimed { get; set; }
    public List<string> Actions { get; set; } = [];
}

/// <summary>
/// Status of an encrypted tag after SUN validation.
/// </summary>
public enum NfcTagClaimStatus
{
    /// <summary>Tag has an owner. Standard profile lookup.</summary>
    HasOwner,
    /// <summary>Tag was unclaimed, now claimed by the observer.</summary>
    JustClaimed,
    /// <summary>Tag is pre-assigned to a different user.</summary>
    PreAssigned,
    /// <summary>Tag is unclaimed and no observer provided.</summary>
    Unclaimed,
    /// <summary>Tag is unclaimed but observer is not authenticated.</summary>
    NeedsAuth,
    /// <summary>Tag is pre-assigned but observer doesn't match.</summary>
    PreAssignedMismatch
}

public record NfcValidationResult(
    SnNfcTag Tag,
    SnAccount? Account,
    SnAccountProfile? Profile,
    bool IsFriend,
    bool IsClaimed,
    NfcTagClaimStatus ClaimStatus,
    List<string> Actions
);

public class NfcService(
    AppDatabase db,
    AccountService accounts,
    RelationshipService relationships,
    ILogger<NfcService> logger
)
{
    /// <summary>
    /// Resolve a UID to a user profile.
    /// The UID is read directly from the NFC tag.
    /// </summary>
    public async Task<NfcResolveResult?> ResolveAsync(
        string uid,
        Guid? observerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Uid == uid.ToUpperInvariant() && t.IsActive, cancellationToken);

        if (tag is null) return null;

        tag.LastSeenAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);

        return await BuildResultAsync(tag, observerUserId, cancellationToken);
    }

    /// <summary>
    /// Look up a tag by its UID and return the associated user profile.
    /// </summary>
    public async Task<NfcResolveResult?> LookupByUidAsync(
        string uid,
        Guid? observerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Uid == uid.ToUpperInvariant() && t.IsActive, cancellationToken);

        if (tag is null) return null;

        return await BuildResultAsync(tag, observerUserId, cancellationToken);
    }

    /// <summary>
    /// Look up a tag by its database entry ID and return the associated user profile.
    /// For unencrypted/plain tags where the entry UUID is stored on the physical tag.
    /// </summary>
    public async Task<NfcResolveResult?> LookupByIdAsync(
        Guid tagId,
        Guid? observerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.IsActive, cancellationToken);

        if (tag is null) return null;

        return await BuildResultAsync(tag, observerUserId, cancellationToken);
    }

    /// <summary>
    /// Validate encrypted SDM scan data.
    /// Decrypts the PICCData using each tag's SDMFileReadKey, extracts UID and counter,
    /// matches the UID to find the tag, and performs claim logic.
    /// </summary>
    /// <param name="uidHex">Full hex-encoded SDM data from the NFC scan URL.</param>
    /// <param name="observerUserId">Optional authenticated user ID.</param>
    public async Task<NfcValidationResult?> ValidateSunAsync(
        string uidHex,
        Guid? observerUserId = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("SUN scan: uidHex={Length} chars, first 32={Preview}",
            uidHex.Length, uidHex.Length >= 32 ? uidHex[..32] : uidHex);

        // Parse the full hex data
        var fullData = NfcCrypto.ParseFullUidData(uidHex, logger);
        if (fullData is null)
        {
            logger.LogWarning("SUN scan: invalid uid_hex format: {Length} chars", uidHex.Length);
            return null;
        }

        // Extract the first 16 bytes as the encrypted PICCData block
        var encryptedPiccData = NfcCrypto.ExtractPiccDataBlock(fullData, logger);
        if (encryptedPiccData is null)
        {
            logger.LogWarning("SUN scan: data too short ({Len} bytes)", fullData.Length);
            return null;
        }

        // Load all active encrypted tags
        var encryptedTags = await db.NfcTags
            .Where(t => t.IsActive && t.IsEncrypted && t.SunKey != null)
            .ToListAsync(cancellationToken);

        logger.LogInformation("SUN scan: trying {Count} encrypted tags", encryptedTags.Count);

        SnNfcTag? matchedTag = null;
        NfcCrypto.SunValidationResult? validation = null;

        foreach (var tag in encryptedTags)
        {
            logger.LogInformation("SUN scan: trying tag {TagId} (DB UID: {TagUid}, key={KeyLen}B)",
                tag.Id, tag.Uid, tag.SunKey!.Length);

            // Try decrypting with this tag's key
            var result = NfcCrypto.DecryptPiccData(tag.SunKey!, encryptedPiccData, logger);
            if (result is null)
            {
                logger.LogInformation("SUN scan: tag {TagId} key did not match", tag.Id);
                continue;
            }

            // Check if the decrypted UID matches this tag
            var decryptedUid = NfcCrypto.FormatUid(result.Uid);
            logger.LogInformation("SUN scan: decrypted UID={Decrypted}, DB UID={TagUid}, match={Match}",
                decryptedUid, tag.Uid, decryptedUid == tag.Uid);

            if (decryptedUid == tag.Uid)
            {
                matchedTag = tag;
                validation = result;
                logger.LogInformation("SUN scan: ✓ MATCHED tag {TagId}", tag.Id);
                break;
            }
        }

        if (matchedTag is null || validation is null)
        {
            logger.LogWarning("SUN scan: no matching tag found for {Len} chars (tried {Count} tags)",
                uidHex.Length, encryptedTags.Count);
            return null;
        }

        var readCtr = validation.ReadCtr;

        // Counter replay check: must be greater than last known counter
        if (matchedTag.Counter.HasValue && readCtr <= matchedTag.Counter.Value)
        {
            logger.LogWarning(
                "SUN replay detected: counter {Counter} <= last counter {LastCounter} for tag {TagId}",
                readCtr, matchedTag.Counter.Value, matchedTag.Id);
            throw new InvalidOperationException(
                $"Replay detected: counter {readCtr} is not greater than last seen counter {matchedTag.Counter.Value}.");
        }

        // Update counter and last seen
        matchedTag.Counter = readCtr;
        matchedTag.LastSeenAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "SUN validated for tag {TagId}, UID {Uid}, counter {Counter}",
            matchedTag.Id, matchedTag.Uid, readCtr);

        // --- Handle tag ownership / claiming ---

        var isClaimed = false;
        var claimStatus = NfcTagClaimStatus.HasOwner;
        SnAccount? account = null;
        SnAccountProfile? profile = null;
        var isFriend = false;
        var actions = new List<string>();

        if (matchedTag.UserId == Guid.Empty || matchedTag.UserId == default)
        {
            // Tag is unclaimed — do NOT auto-claim.
            // User must explicitly claim via POST /api/nfc/tags/claim
            if (observerUserId.HasValue)
            {
                // Authenticated user scanned unclaimed tag — return unclaimed status
                return new NfcValidationResult(matchedTag, null, null, false, false, NfcTagClaimStatus.Unclaimed, ["claim_tag"]);
            }
            else
            {
                // No authenticated user — return needs auth status
                return new NfcValidationResult(matchedTag, null, null, false, false, NfcTagClaimStatus.NeedsAuth, []);
            }
        }
        else if (observerUserId.HasValue && observerUserId.Value != matchedTag.UserId)
        {
            // Observer is different from tag owner
            claimStatus = NfcTagClaimStatus.PreAssignedMismatch;
        }

        // Build result — fetch the tag owner's account
        account = await accounts.GetAccount(matchedTag.UserId);
        if (account is null)
        {
            return new NfcValidationResult(matchedTag, null, null, false, isClaimed, claimStatus, []);
        }

        profile = account.Profile;
        actions = ["view_profile"];

        if (observerUserId.HasValue && observerUserId.Value != matchedTag.UserId)
        {
            // Check if blocked
            var blocked =
                await relationships.HasRelationshipWithStatus(observerUserId.Value, matchedTag.UserId,
                    RelationshipStatus.Blocked) ||
                await relationships.HasRelationshipWithStatus(matchedTag.UserId, observerUserId.Value,
                    RelationshipStatus.Blocked);

            if (blocked)
            {
                // Blocked — don't return profile, but still return tag info for the gRPC login path
                return new NfcValidationResult(matchedTag, account, profile, false, isClaimed, claimStatus, []);
            }

            isFriend = await relationships.HasRelationshipWithStatus(
                observerUserId.Value, matchedTag.UserId);
            actions.Add("add_friend");
        }

        return new NfcValidationResult(matchedTag, account, profile, isFriend, isClaimed, claimStatus, actions);
    }

    /// <summary>
    /// Register a new unencrypted NFC tag for a user.
    /// The user writes the tag entry ID to the physical tag.
    /// </summary>
    public async Task<SnNfcTag> RegisterTagAsync(
        Guid userId,
        string uid,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Uid == uid.ToUpperInvariant(), cancellationToken);

        if (existing is not null)
            throw new InvalidOperationException("This NFC tag is already registered.");

        var tag = new SnNfcTag
        {
            Uid = uid.ToUpperInvariant(),
            UserId = userId,
            Label = label,
            IsEncrypted = false
        };

        db.NfcTags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("NFC tag {TagId} registered for user {UserId} (unencrypted)", tag.Id, userId);

        return tag;
    }

    /// <summary>
    /// Factory/Admin: Register an encrypted NFC tag with a pre-generated SUN key.
    /// The tag is registered without an owner (unassigned) until a user claims it by scanning.
    /// Optionally pre-assign to a specific user.
    /// </summary>
    public async Task<SnNfcTag> RegisterEncryptedTagAsync(
        string uid,
        byte[] sunKey,
        Guid? assignedUserId = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Uid == uid.ToUpperInvariant(), cancellationToken);

        if (existing is not null)
            throw new InvalidOperationException("This NFC tag is already registered.");

        if (sunKey.Length != 16 && sunKey.Length != 32)
            throw new ArgumentException("SUN key must be 16 bytes (AES-128) or 32 bytes (AES-256).");

        var tag = new SnNfcTag
        {
            Uid = uid.ToUpperInvariant(),
            UserId = assignedUserId ?? Guid.Empty,
            Label = null,
            IsEncrypted = true,
            SunKey = sunKey,
            Counter = 0
        };

        db.NfcTags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Encrypted NFC tag {TagId} registered (UID: {Uid}, assigned: {Assigned})",
            tag.Id, uid, assignedUserId?.ToString() ?? "unassigned");

        return tag;
    }

    /// <summary>
    /// Claim an unclaimed encrypted NFC tag by UID (without scanning).
    /// The user provides the tag's UID (e.g., printed on the tag).
    /// </summary>
    /// <param name="uid">Tag UID (hex string, e.g., "04A1B2C3D4E5F6").</param>
    /// <param name="userId">User claiming the tag.</param>
    public async Task<SnNfcTag> ClaimTagByUidAsync(
        string uid,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var normalizedUid = uid.ToUpperInvariant();

        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Uid == normalizedUid && t.IsEncrypted && t.IsActive, cancellationToken);

        if (tag is null)
            throw new InvalidOperationException("Encrypted tag not found with this UID.");

        // Check if already claimed by this user
        if (tag.UserId == userId)
            return tag; // Already their tag, return as-is

        // Check if already claimed by someone else
        if (tag.UserId != Guid.Empty && tag.UserId != default)
            throw new InvalidOperationException("This tag has already been claimed by another account.");

        // Claim the tag
        tag.UserId = userId;
        tag.LastSeenAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Tag {TagId} (UID: {Uid}) claimed by user {UserId} via UID", tag.Id, uid, userId);

        return tag;
    }

    /// <summary>
    /// Factory/Admin: List all encrypted tags (for management).
    /// </summary>
    public async Task<List<SnNfcTag>> ListAllEncryptedTagsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.NfcTags
            .Where(t => t.IsEncrypted && t.IsActive)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Unregister (soft-delete) an NFC tag.
    /// </summary>
    public async Task<bool> UnregisterTagAsync(
        Guid userId,
        Guid tagId,
        CancellationToken cancellationToken = default)
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId, cancellationToken);

        if (tag is null) return false;

        tag.IsActive = false;
        tag.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("NFC tag {TagId} unregistered by user {UserId}", tagId, userId);
        return true;
    }

    /// <summary>
    /// List all active NFC tags for a user.
    /// </summary>
    public async Task<List<SnNfcTag>> ListTagsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await db.NfcTags
            .Where(t => t.UserId == userId && t.IsActive)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Update tag metadata (label, active status).
    /// </summary>
    public async Task<SnNfcTag?> UpdateTagAsync(
        Guid userId,
        Guid tagId,
        string? label = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId, cancellationToken);

        if (tag is null) return null;

        if (label is not null) tag.Label = label;
        if (isActive.HasValue) tag.IsActive = isActive.Value;

        await db.SaveChangesAsync(cancellationToken);
        return tag;
    }

    /// <summary>
    /// Lock a tag to prevent physical reprogramming.
    /// </summary>
    public async Task<SnNfcTag?> LockTagAsync(
        Guid userId,
        Guid tagId,
        CancellationToken cancellationToken = default)
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId, cancellationToken);

        if (tag is null) return null;

        tag.LockedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("NFC tag {TagId} locked by user {UserId}", tagId, userId);
        return tag;
    }

    private async Task<NfcResolveResult?> BuildResultAsync(
        SnNfcTag tag,
        Guid? observerUserId,
        CancellationToken cancellationToken)
    {
        var account = await accounts.GetAccount(tag.UserId);
        if (account is null) return null;

        var profile = account.Profile;
        var isFriend = false;

        if (observerUserId.HasValue && observerUserId.Value != tag.UserId)
        {
            isFriend = await relationships.HasRelationshipWithStatus(observerUserId.Value, tag.UserId);

            var blocked =
                await relationships.HasRelationshipWithStatus(observerUserId.Value, tag.UserId,
                    RelationshipStatus.Blocked) ||
                await relationships.HasRelationshipWithStatus(tag.UserId, observerUserId.Value,
                    RelationshipStatus.Blocked);
            if (blocked) return null;
        }

        var actions = new List<string> { "view_profile" };
        if (observerUserId.HasValue && observerUserId.Value != tag.UserId)
            actions.Add("add_friend");

        return new NfcResolveResult
        {
            User = account,
            Profile = profile,
            IsFriend = isFriend,
            Actions = actions
        };
    }
}
