using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class PostTagService(AppDatabase db, ILogger<PostTagService> logger)
{
    public async Task<SnPostTag> CreateTagAsync(
        string slug,
        string? name = null,
        string? description = null,
        SnPublisher? owner = null
    )
    {
        var normalizedSlug = NormalizeSlug(slug);
        var existing = await db.PostTags.FirstOrDefaultAsync(t => t.Slug == normalizedSlug);
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

        if (isProtected && tag.OwnerPublisherId != publisher.Id)
            throw new InvalidOperationException("Only the tag owner can protect it.");

        if (isProtected)
        {
            var account = await db.Publishers
                .Where(p => p.Id == publisher.Id)
                .Select(p => p.Account)
                .FirstOrDefaultAsync();

            if (account is null)
                throw new InvalidOperationException("Cannot resolve account for publisher.");

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

        if (isEvent && endsAt.Value <= SystemClock.Instance.GetCurrentInstant())
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
        var account = await db.Publishers
            .Where(p => p.Id == publisher.Id)
            .Select(p => p.Account)
            .FirstOrDefaultAsync();

        var level = account?.Profile?.Level ?? 0;
        var perkLevel = account?.PerkLevel ?? 0;
        var total = ResourceQuotaCalculator.GetProtectedTagQuota(level, perkLevel);

        var protectedTags = await db.PostTags
            .Where(t => t.OwnerPublisherId == publisher.Id && t.IsProtected)
            .Select(t => new ProtectedTagQuotaRecord
            {
                Id = t.Id,
                Slug = t.Slug,
                Name = t.Name,
            })
            .ToListAsync();

        return new ResourceQuotaResponse<ProtectedTagQuotaRecord>
        {
            Total = total,
            Used = protectedTags.Count,
            Remaining = Math.Max(0, total - protectedTags.Count),
            Level = level,
            PerkLevel = perkLevel,
            Records = protectedTags,
        };
    }

    public async Task<SnPostTag?> FindBySlugAsync(string slug)
    {
        var normalized = NormalizeSlug(slug);
        return await db.PostTags.FirstOrDefaultAsync(t => t.Slug == normalized);
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
