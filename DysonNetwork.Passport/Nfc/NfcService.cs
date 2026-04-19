using System.Security.Cryptography;
using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Nfc;

public class NfcResolveResult
{
    public Guid Id { get; set; }
    public SnAccount Account { get; set; } = null!;
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
        CancellationToken cancellationToken = default
    )
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Uid == uid.ToUpper() && t.IsActive, cancellationToken);

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
        CancellationToken cancellationToken = default
    )
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Uid == uid.ToUpper() && t.IsActive, cancellationToken);

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
    /// Validate encrypted SDM scan data using NTAG 424 DNA SDM protocol.
    /// Decrypts the PICCData using SDMMetaReadKey, extracts UID and counter,
    /// matches the UID to find the tag, verifies CMAC, and performs claim logic.
    ///
    /// URL format: solian://phpass?picc_data=...&e=...&cmac=...
    /// </summary>
    /// <param name="piccDataHex">Encrypted PICCData hex (16 bytes, from picc_data parameter).</param>
    /// <param name="encDataHex">Encrypted file data hex (from e parameter), optional.</param>
    /// <param name="cmacHex">Truncated SDM CMAC hex (8 bytes, from cmac parameter), optional.</param>
    /// <param name="observerUserId">Optional authenticated user ID.</param>
    public async Task<NfcValidationResult?> ValidateSunAsync(
        string piccDataHex,
        string? encDataHex,
        string? cmacHex,
        Guid? observerUserId = null,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation("SUN scan: picc_data={PiccData}, e={EncData}, cmac={Cmac}",
            piccDataHex, encDataHex ?? "(none)", cmacHex ?? "(none)");

        // Parse hex inputs
        byte[] encryptedPiccData;
        try
        {
            encryptedPiccData = Convert.FromHexString(piccDataHex);
        }
        catch (FormatException)
        {
            logger.LogWarning("SUN scan: invalid picc_data hex format");
            return null;
        }

        if (encryptedPiccData.Length != 16)
        {
            logger.LogWarning("SUN scan: picc_data must be exactly 16 bytes, got {Len}", encryptedPiccData.Length);
            return null;
        }

        byte[]? encData = null;
        if (!string.IsNullOrEmpty(encDataHex))
        {
            try
            {
                encData = Convert.FromHexString(encDataHex);
                if (encData.Length % 16 != 0)
                {
                    logger.LogWarning("SUN scan: enc data length must be multiple of 16");
                    return null;
                }
            }
            catch (FormatException)
            {
                logger.LogWarning("SUN scan: invalid e hex format");
                return null;
            }
        }

        byte[]? cmac = null;
        if (!string.IsNullOrEmpty(cmacHex))
        {
            try
            {
                cmac = Convert.FromHexString(cmacHex);
                if (cmac.Length != 8)
                {
                    logger.LogWarning("SUN scan: cmac must be exactly 8 bytes, got {Len}", cmac.Length);
                    return null;
                }
            }
            catch (FormatException)
            {
                logger.LogWarning("SUN scan: invalid cmac hex format");
                return null;
            }
        }

        // Load all active encrypted tags to try decryption
        // We need to try each tag's key since we don't know which tag until we decrypt
        var encryptedTags = await db.NfcTags
            .Where(t => t.IsActive && t.IsEncrypted && t.SunKey != null)
            .ToListAsync(cancellationToken);

        logger.LogInformation("SUN scan: trying {Count} encrypted tags", encryptedTags.Count);

        foreach (var tag in encryptedTags)
        {
            // For now, use SunKey as both MetaKey and FileKey
            // TODO: Add separate MetaKey field to SnNfcTag if keys are different
            var metaKey = tag.SunKey!;
            var fileKey = tag.SunKey!;

            // Step 1: Decrypt PICCData with SDMMetaReadKey using AES-CBC with zero IV
            var piccPlain = NfcCrypto.DecryptPiccDataWithMetaKey(metaKey, encryptedPiccData, logger);
            if (piccPlain is null)
            {
                logger.LogDebug("SUN scan: tag {TagId} meta key did not match", tag.Id);
                continue;
            }

            // Step 2: Parse PICCData to extract UID and SDMReadCtr
            var piccData = NfcCrypto.ParsePiccData(piccPlain, logger);
            if (piccData is null)
            {
                logger.LogWarning("SUN scan: failed to parse PICCData for tag {TagId}", tag.Id);
                continue;
            }

            if (piccData.Uid is null || piccData.ReadCtrLsb is null || piccData.ReadCtrInt is null)
            {
                logger.LogWarning("SUN scan: PICCData missing UID or counter for tag {TagId}", tag.Id);
                continue;
            }

            var decryptedUid = NfcCrypto.FormatUid(piccData.Uid);
            var readCtr = piccData.ReadCtrInt.Value;

            logger.LogInformation("SUN scan: tag {TagId} decrypted UID={Uid}, counter={Ctr}",
                tag.Id, decryptedUid, readCtr);

            // Check if the decrypted UID matches this tag's UID
            if (!string.Equals(decryptedUid, tag.Uid, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("SUN scan: decrypted UID {DecryptedUid} doesn't match tag UID {TagUid}",
                    decryptedUid, tag.Uid);
                continue;
            }

            // Step 3: Derive session keys from SDMFileReadKey + UID + SDMReadCtr
            var (ksesEnc, ksesMac) = NfcCrypto.DeriveSessionKeys(fileKey, piccData.Uid, piccData.ReadCtrLsb, logger);

            // Step 4: Verify CMAC if provided
            if (cmac is not null)
            {
                // Build MAC input: enc_hex + "&cmac="
                // This is the default configuration where MAC covers data between SDMMACInputOffset and SDMMACOffset
                var macInputStr = $"{encDataHex!.ToUpperInvariant()}&cmac=";
                var macInput = System.Text.Encoding.ASCII.GetBytes(macInputStr);

                var cmacValid = NfcCrypto.VerifyCmac(ksesMac, macInput, cmac, logger);
                if (!cmacValid)
                {
                    logger.LogWarning("SUN scan: CMAC verification failed for tag {TagId}", tag.Id);
                    continue;
                }
                logger.LogInformation("SUN scan: CMAC verified for tag {TagId}", tag.Id);
            }

            // Step 5: Decrypt file data if provided (optional - for debugging/info)
            if (encData is not null)
            {
                var ive = NfcCrypto.BuildIve(ksesEnc, piccData.ReadCtrLsb, logger);
                var decryptedFileData = NfcCrypto.DecryptFileData(ksesEnc, encData, ive, logger);
                if (decryptedFileData is not null)
                {
                    try
                    {
                        var asciiData = System.Text.Encoding.UTF8.GetString(decryptedFileData);
                        logger.LogInformation("SUN scan: decrypted file data (UTF-8): {Data}", asciiData);
                    }
                    catch
                    {
                        logger.LogInformation("SUN scan: decrypted file data (hex): {Data}",
                            Convert.ToHexString(decryptedFileData));
                    }
                }
            }

            // Counter replay check: must be strictly greater than last known counter
            if (tag.Counter.HasValue && readCtr < tag.Counter.Value)
            {
                logger.LogWarning(
                    "SUN replay detected: counter {Counter} <= last counter {LastCounter} for tag {TagId}",
                    readCtr, tag.Counter.Value, tag.Id);
                throw new InvalidOperationException(
                    $"Replay detected: counter {readCtr} is not greater than last seen counter {tag.Counter.Value}.");
            }

            // Update counter and last seen
            tag.Counter = readCtr;
            tag.LastSeenAt = SystemClock.Instance.GetCurrentInstant();
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "SUN validated for tag {TagId}, UID {Uid}, counter {Counter}",
                tag.Id, tag.Uid, readCtr);

            // --- Handle tag ownership / claiming ---

            var isClaimed = false;
            var claimStatus = NfcTagClaimStatus.HasOwner;
            SnAccount? account = null;
            SnAccountProfile? profile = null;
            var isFriend = false;
            var actions = new List<string>();

            if (!tag.AccountId.HasValue)
            {
                // Tag is unclaimed — do NOT auto-claim.
                // User must explicitly claim via POST /api/nfc/tags/claim
                if (observerUserId.HasValue)
                {
                    // Authenticated user scanned unclaimed tag — return unclaimed status
                    return new NfcValidationResult(tag, null, null, false, false, NfcTagClaimStatus.Unclaimed, ["claim_tag"]);
                }
                else
                {
                    // No authenticated user — return needs auth status
                    return new NfcValidationResult(tag, null, null, false, false, NfcTagClaimStatus.NeedsAuth, []);
                }
            }
            else if (observerUserId.HasValue && observerUserId.Value != tag.AccountId)
            {
                // Observer is different from tag owner - this is pre-assigned to someone else
                claimStatus = NfcTagClaimStatus.PreAssignedMismatch;
            }
            // If observerUserId matches tag.AccountId, claimStatus stays as HasOwner (normal case)

            // Build result — fetch the tag owner's account
            if (!tag.AccountId.HasValue)
            {
                return new NfcValidationResult(tag, null, null, false, isClaimed, claimStatus, []);
            }

            account = await accounts.GetAccount(tag.AccountId.Value);
            if (account is null)
            {
                return new NfcValidationResult(tag, null, null, false, isClaimed, claimStatus, []);
            }

            profile = account.Profile;
            actions = ["view_profile"];

            if (observerUserId.HasValue && observerUserId.Value != tag.AccountId.Value)
            {
                // Check if blocked
                var blocked =
                    await relationships.HasRelationshipWithStatus(observerUserId.Value, tag.AccountId!.Value,
                        RelationshipStatus.Blocked) ||
                    await relationships.HasRelationshipWithStatus(tag.AccountId!.Value, observerUserId.Value,
                        RelationshipStatus.Blocked);

                if (blocked)
                {
                    // Blocked — don't return profile, but still return tag info for the gRPC login path
                    return new NfcValidationResult(tag, account, profile, false, isClaimed, claimStatus, []);
                }

                isFriend = await relationships.HasRelationshipWithStatus(
                    observerUserId.Value, tag.AccountId!.Value);
                actions.Add("add_friend");
            }

            return new NfcValidationResult(tag, account, profile, isFriend, isClaimed, claimStatus, actions);
        }

        logger.LogWarning("SUN scan: no matching tag found");
        return null;
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
            AccountId = userId,
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
            AccountId = assignedUserId,
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
        var tag = await GetEncryptedTagByUidAsync(uid, cancellationToken);

        if (tag is null)
            throw new InvalidOperationException("Encrypted tag not found with this UID.");

        var claimedTag = await ClaimTagInternalAsync(tag, userId, cancellationToken);

        logger.LogInformation("Tag {TagId} (UID: {Uid}) claimed by user {UserId} via UID", tag.Id, uid, userId);

        return claimedTag;
    }

    private async Task<SnNfcTag?> GetEncryptedTagByUidAsync(
        string uid,
        CancellationToken cancellationToken = default)
    {
        var normalizedUid = uid.ToUpperInvariant();

        return await db.NfcTags
            .FirstOrDefaultAsync(t => t.Uid == normalizedUid && t.IsEncrypted && t.IsActive, cancellationToken);
    }

    private async Task<SnNfcTag> ClaimTagInternalAsync(
        SnNfcTag tag,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (tag.AccountId == userId)
            return tag;

        if (tag.AccountId.HasValue && tag.AccountId.Value != userId)
            throw new InvalidOperationException("This tag has already been claimed by another account.");

        tag.AccountId = userId;
        tag.LastSeenAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);

        return tag;
    }

    /// <summary>
    /// Claim an unclaimed encrypted NFC tag by ID.
    /// </summary>
    /// <param name="tagId">Tag ID (primary key).</param>
    /// <param name="userId">User claiming the tag.</param>
    public async Task<SnNfcTag> ClaimTagByIdAsync(
        Guid tagId,
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.IsEncrypted && t.IsActive, cancellationToken);

        if (tag is null)
            throw new InvalidOperationException("Encrypted tag not found with this ID.");

        var claimedTag = await ClaimTagInternalAsync(tag, userId, cancellationToken);

        logger.LogInformation("Tag {TagId} claimed by user {UserId} via ID", tagId, userId);

        return claimedTag;
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
            .FirstOrDefaultAsync(t => t.Id == tagId && t.AccountId == userId, cancellationToken);

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
            .Where(t => t.AccountId == userId && t.IsActive)
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
            .FirstOrDefaultAsync(t => t.Id == tagId && t.AccountId == userId, cancellationToken);

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
            .FirstOrDefaultAsync(t => t.Id == tagId && t.AccountId == userId, cancellationToken);

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
        if (!tag.AccountId.HasValue) return null;

        var account = await accounts.GetAccount(tag.AccountId.Value);
        if (account is null) return null;

        var isFriend = false;

        if (observerUserId.HasValue && observerUserId.Value != tag.AccountId.Value)
        {
            isFriend = await relationships.HasRelationshipWithStatus(observerUserId.Value, tag.AccountId.Value);

            var blocked =
                await relationships.HasRelationshipWithStatus(observerUserId.Value, tag.AccountId.Value,
                    RelationshipStatus.Blocked) ||
                await relationships.HasRelationshipWithStatus(tag.AccountId.Value, observerUserId.Value,
                    RelationshipStatus.Blocked);
            if (blocked) return null;
        }

        var actions = new List<string> { "view_profile" };
        if (observerUserId.HasValue && observerUserId.Value != tag.AccountId.Value)
            actions.Add("add_friend");

        return new NfcResolveResult
        {
            Id = tag.Id,
            Account = account,
            IsFriend = isFriend,
            Actions = actions
        };
    }
}
