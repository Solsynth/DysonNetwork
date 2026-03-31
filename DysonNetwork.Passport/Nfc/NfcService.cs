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
    public List<string> Actions { get; set; } = [];
}

public record NfcValidationResult(
    SnNfcTag Tag,
    SnAccount Account,
    SnAccountProfile? Profile,
    bool IsFriend,
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
    /// Validate NTAG424 SUN parameters (encrypted scan).
    /// Verifies the CMAC, decrypts the PICCData to extract UID, checks counter for replay,
    /// updates the counter, and returns the tag + account.
    /// </summary>
    /// <param name="eBase64">Base64-encoded encrypted PICCData from the SUN URL.</param>
    /// <param name="readCtr">Read counter from the SUN URL parameter 'c'.</param>
    /// <param name="macBase64">Base64-encoded CMAC from the SUN URL.</param>
    /// <param name="observerUserId">Optional authenticated user ID for relationship checks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with tag and account info, or null if no matching encrypted tag.</returns>
    /// <exception cref="CryptographicException">Thrown if crypto verification fails (MAC mismatch, invalid data).</exception>
    /// <exception cref="InvalidOperationException">Thrown if counter replay is detected.</exception>
    public async Task<NfcValidationResult?> ValidateSunAsync(
        string eBase64,
        int readCtr,
        string macBase64,
        Guid? observerUserId = null,
        CancellationToken cancellationToken = default)
    {
        // Load all active encrypted tags
        var encryptedTags = await db.NfcTags
            .Where(t => t.IsActive && t.IsEncrypted && t.SunKey != null)
            .ToListAsync(cancellationToken);

        SnNfcTag? matchedTag = null;
        NfcCrypto.SunValidationResult? validation = null;

        foreach (var tag in encryptedTags)
        {
            try
            {
                var result = NfcCrypto.Validate(tag.SunKey!, eBase64, readCtr, macBase64);

                // Verify the decrypted UID matches this tag
                var decryptedUid = FormatUid(result.Uid);
                if (decryptedUid == tag.Uid)
                {
                    matchedTag = tag;
                    validation = result;
                    break;
                }
            }
            catch (CryptographicException)
            {
                // MAC doesn't match this tag's key, try next one
                continue;
            }
        }

        if (matchedTag is null || validation is null)
        {
            logger.LogWarning("SUN validation: no matching encrypted tag found");
            return null;
        }

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

        // Build result with relationship info
        var account = await accounts.GetAccount(matchedTag.UserId);
        if (account is null) return null;

        var profile = account.Profile;
        var isFriend = false;
        var actions = new List<string> { "view_profile" };

        if (observerUserId.HasValue && observerUserId.Value != matchedTag.UserId)
        {
            isFriend = await relationships.HasRelationshipWithStatus(
                observerUserId.Value, matchedTag.UserId);

            var blocked =
                await relationships.HasRelationshipWithStatus(observerUserId.Value, matchedTag.UserId,
                    RelationshipStatus.Blocked) ||
                await relationships.HasRelationshipWithStatus(matchedTag.UserId, observerUserId.Value,
                    RelationshipStatus.Blocked);

            if (blocked) return null;

            actions.Add("add_friend");
        }

        return new NfcValidationResult(matchedTag, account, profile, isFriend, actions);
    }

    /// <summary>
    /// Register a new NFC tag for a user.
    /// For encrypted tags, generates a random SUN key and stores it.
    /// </summary>
    /// <param name="userId">Owner account ID.</param>
    /// <param name="uid">Chip UID (hex string, e.g. "04A1B2C3D4E5F6").</param>
    /// <param name="label">Optional user-facing label.</param>
    /// <param name="isEncrypted">Whether this is an encrypted NTAG424 SUN tag.</param>
    /// <returns>The created tag. For encrypted tags, SunKey is populated with the generated key.</returns>
    public async Task<SnNfcTag> RegisterTagAsync(
        Guid userId,
        string uid,
        string? label = null,
        bool isEncrypted = false,
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
            IsEncrypted = isEncrypted
        };

        if (isEncrypted)
        {
            // Generate a random 16-byte AES-128 SUN key
            tag.SunKey = new byte[16];
            RandomNumberGenerator.Fill(tag.SunKey);
            tag.Counter = 0;
        }

        db.NfcTags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "NFC tag {TagId} registered for user {UserId}, encrypted={IsEncrypted}",
            tag.Id, userId, isEncrypted);

        return tag;
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

    /// <summary>
    /// Convert a 7-byte UID to an uppercase hex string (e.g., "04A1B2C3D4E5F6").
    /// </summary>
    private static string FormatUid(byte[] uid)
    {
        return BitConverter.ToString(uid).Replace("-", "");
    }
}
