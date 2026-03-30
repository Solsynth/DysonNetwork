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
    /// Register a new NFC tag for a user.
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
            Label = label
        };

        db.NfcTags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("NFC tag {TagId} registered for user {UserId}", tag.Id, userId);
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
}
