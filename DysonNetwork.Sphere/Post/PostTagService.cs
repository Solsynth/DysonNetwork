using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class PostTagService(AppDatabase db, RemoteAccountService remoteAccounts)
{
    public async Task<SnPostTag> CreateTagAsync(
        string slug,
        string? name = null,
        string? description = null,
        SnPublisher? owner = null
    )
    {
        var normalizedSlug = NormalizeSlug(slug);
        var existing = await db.PostTags.FirstOrDefaultAsync(t => t.Slug.ToLower() == normalizedSlug);
        if (existing is not null)
            throw new InvalidOperationException("A tag with this slug already exists.");

        var tag = new SnPostTag
        {
            Slug = normalizedSlug,
            Name = name,
            Description = description,
            OwnerPublisherId = owner?.Id,
        };

        db.PostTags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task<SnPostTag> UpdateTagAsync(
        Guid tagId,
        string? name,
        string? description,
        Guid accountId,
        bool isAdmin
    )
    {
        var tag = await db.PostTags
            .FirstOrDefaultAsync(t => t.Id == tagId)
            ?? throw new InvalidOperationException("Tag not found.");

        if (!isAdmin)
        {
            if (tag.OwnerPublisherId is null)
                throw new InvalidOperationException("This tag has no owner. Only admin can edit it.");

            var isOwner = await db.Publishers
                .Where(p => p.Id == tag.OwnerPublisherId.Value)
                .SelectMany(p => p.Members)
                .AnyAsync(m => m.AccountId == accountId && m.Role >= PublisherMemberRole.Manager);

            if (!isOwner)
                throw new InvalidOperationException("You must be a manager or above of the owning publisher to edit this tag.");
        }

        if (name is not null) tag.Name = name;
        if (description is not null) tag.Description = description;

        await db.SaveChangesAsync();
        return tag;
    }

    public async Task<SnPostTag> ClaimTagAsync(Guid tagId, SnPublisher publisher)
    {
        var tag = await db.PostTags.FirstOrDefaultAsync(t => t.Id == tagId)
            ?? throw new InvalidOperationException("Tag not found.");

        if (tag.OwnerPublisherId is not null)
            throw new InvalidOperationException("This tag is already owned by a publisher.");

        tag.OwnerPublisherId = publisher.Id;
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task<SnPostTag> AssignTagAsync(Guid tagId, Guid publisherId)
    {
        var tag = await db.PostTags.FirstOrDefaultAsync(t => t.Id == tagId)
            ?? throw new InvalidOperationException("Tag not found.");

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Id == publisherId)
            ?? throw new InvalidOperationException("Publisher not found.");

        tag.OwnerPublisherId = publisher.Id;
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task<SnPostTag> SetProtectedAsync(Guid tagId, bool isProtected, SnPublisher publisher)
    {
        var tag = await db.PostTags.FirstOrDefaultAsync(t => t.Id == tagId)
            ?? throw new InvalidOperationException("Tag not found.");

        if (isProtected && tag.OwnerPublisherId is null)
            throw new InvalidOperationException("Cannot protect a tag that has no owner.");

        if (tag.OwnerPublisherId is not null && tag.OwnerPublisherId != publisher.Id)
            throw new InvalidOperationException("Only the tag owner can change protection.");

        // Enabling protection consumes a quota slot (skip if already protected).
        if (isProtected && !tag.IsProtected)
        {
            var quota = await GetProtectedTagQuotaAsync(publisher);
            if (quota.Used >= quota.Total)
                throw new InvalidOperationException($"Protected tag quota exceeded ({quota.Used}/{quota.Total}).");
        }

        tag.IsProtected = isProtected;
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task<SnPostTag> SetEventAsync(Guid tagId, bool isEvent, Instant? endsAt)
    {
        var tag = await db.PostTags.FirstOrDefaultAsync(t => t.Id == tagId)
            ?? throw new InvalidOperationException("Tag not found.");

        if (isEvent && endsAt is null)
            throw new InvalidOperationException("Event tags must have an end time.");

        if (isEvent && endsAt!.Value <= SystemClock.Instance.GetCurrentInstant())
            throw new InvalidOperationException("Event end time must be in the future.");

        tag.IsEvent = isEvent;
        tag.EventEndsAt = isEvent ? endsAt : null;
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task ValidateTagUsageAsync(SnPostTag tag, SnPublisher? publisher)
    {
        if (tag.IsEvent && tag.EventEndsAt is not null)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            if (tag.EventEndsAt.Value <= now)
                throw new InvalidOperationException($"Tag '{tag.Slug}' is an event tag that has expired.");
        }

        if (tag.IsProtected && tag.OwnerPublisherId is not null && publisher is not null)
        {
            if (tag.OwnerPublisherId.Value != publisher.Id)
                throw new InvalidOperationException($"Tag '{tag.Slug}' is protected and can only be used by its owner.");
        }
    }

    public async Task<ResourceQuotaResponse<ProtectedTagQuotaRecord>> GetProtectedTagQuotaAsync(SnPublisher publisher)
    {
        // SnPublisher.Account is NotMapped (Passport is remote). Resolve perk via gRPC.
        var (level, perkLevel) = await ResolvePublisherPerkAsync(publisher);
        var total = ResourceQuotaCalculator.GetProtectedTagQuota(level, perkLevel);

        // Return all owned tags. Usage counts protected tags only.
        var ownedTags = await db.PostTags
            .AsNoTracking()
            .Where(t => t.OwnerPublisherId == publisher.Id)
            .OrderByDescending(t => t.IsProtected)
            .ThenByDescending(t => t.UpdatedAt)
            .Select(t => new ProtectedTagQuotaRecord
            {
                Id = t.Id,
                Slug = t.Slug,
                Name = t.Name,
                Description = t.Description,
                IsProtected = t.IsProtected,
                IsEvent = t.IsEvent,
                EventEndsAt = t.EventEndsAt,
            })
            .ToListAsync();

        var used = ownedTags.Count(t => t.IsProtected);

        return new ResourceQuotaResponse<ProtectedTagQuotaRecord>
        {
            Total = total,
            Used = used,
            Remaining = Math.Max(0, total - used),
            Level = level,
            PerkLevel = perkLevel,
            Records = ownedTags,
        };
    }

    /// <summary>
    /// Resolve account level / perk for a publisher (individual owner, else highest-role member).
    /// </summary>
    private async Task<(int Level, int PerkLevel)> ResolvePublisherPerkAsync(SnPublisher publisher)
    {
        Guid? accountId = publisher.AccountId;
        if (accountId is null)
        {
            accountId = await db.PublisherMembers
                .AsNoTracking()
                .Where(m => m.PublisherId == publisher.Id && m.JoinedAt != null)
                .OrderByDescending(m => m.Role)
                .Select(m => (Guid?)m.AccountId)
                .FirstOrDefaultAsync();
        }

        if (accountId is null)
            return (0, 0);

        var dyAccount = await remoteAccounts.TryGetAccount(accountId.Value);
        if (dyAccount is null)
            return (0, 0);

        var account = SnAccount.FromProtoValue(dyAccount);
        return (account.Profile?.Level ?? 0, account.PerkLevel);
    }

    public async Task<SnPostTag?> FindBySlugAsync(string slug)
    {
        var normalized = NormalizeSlug(slug);
        return await db.PostTags
            .Include(t => t.OwnerPublisher)
            .FirstOrDefaultAsync(t => t.Slug.ToLower() == normalized);
    }

    public async Task<SnPostTag?> FindByIdAsync(Guid id)
    {
        return await db.PostTags
            .Include(t => t.OwnerPublisher)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<SnPostTag> UnassignTagAsync(Guid tagId)
    {
        var tag = await db.PostTags.FirstOrDefaultAsync(t => t.Id == tagId)
            ?? throw new InvalidOperationException("Tag not found.");

        if (tag.IsProtected)
            tag.IsProtected = false;

        tag.OwnerPublisherId = null;
        await db.SaveChangesAsync();
        return tag;
    }

    /// <summary>
    /// Owner publisher releases ownership. Clears protection if set, then unassigns.
    /// Caller must be a manager (or above) of <paramref name="publisher"/>.
    /// </summary>
    public async Task<SnPostTag> ReleaseTagAsync(Guid tagId, SnPublisher publisher, Guid accountId)
    {
        var tag = await db.PostTags.FirstOrDefaultAsync(t => t.Id == tagId)
            ?? throw new InvalidOperationException("Tag not found.");

        if (tag.OwnerPublisherId is null)
            throw new InvalidOperationException("This tag has no owner.");

        if (tag.OwnerPublisherId != publisher.Id)
            throw new InvalidOperationException("This tag is not owned by the specified publisher.");

        var isManager = await db.Publishers
            .Where(p => p.Id == publisher.Id)
            .SelectMany(p => p.Members)
            .AnyAsync(m => m.AccountId == accountId && m.Role >= PublisherMemberRole.Manager);

        if (!isManager)
            throw new InvalidOperationException(
                "You must be a manager or above of the owning publisher to release this tag.");

        return await UnassignTagAsync(tagId);
    }

    public async Task DeleteTagAsync(Guid tagId)
    {
        var tag = await db.PostTags
            .Include(t => t.Posts)
            .FirstOrDefaultAsync(t => t.Id == tagId)
            ?? throw new InvalidOperationException("Tag not found.");

        tag.Posts.Clear();
        db.PostTags.Remove(tag);
        await db.SaveChangesAsync();
    }

    public bool IsTagAvailable(SnPostTag tag)
    {
        if (tag.IsEvent && tag.EventEndsAt is not null)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            return tag.EventEndsAt.Value > now;
        }
        return true;
    }

    private static string NormalizeSlug(string value) =>
        value.Trim().ToLowerInvariant();
}
