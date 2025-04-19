using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class PostService(AppDatabase db, FileService fs)
{
    public async Task<Post> PostAsync(
        Post post,
        List<string>? attachments = null,
        List<string>? tags = null,
        List<string>? categories = null
    )
    {
        if (attachments is not null)
        {
            post.Attachments = await db.Files.Where(e => attachments.Contains(e.Id)).ToListAsync();
            // Re-order the list to match the id list places
            post.Attachments = attachments
                .Select(id => post.Attachments.First(a => a.Id == id))
                .ToList();
        }

        if (tags is not null)
        {
            var existingTags = await db.PostTags.Where(e => tags.Contains(e.Slug)).ToListAsync();

            // Determine missing slugs
            var existingSlugs = existingTags.Select(t => t.Slug).ToHashSet();
            var missingSlugs = tags.Where(slug => !existingSlugs.Contains(slug)).ToList();

            var newTags = missingSlugs.Select(slug => new PostTag { Slug = slug }).ToList();
            if (newTags.Count > 0)
            {
                await db.PostTags.AddRangeAsync(newTags);
                await db.SaveChangesAsync();
            }

            post.Tags = existingTags.Concat(newTags).ToList();
        }

        if (categories is not null)
        {
            post.Categories = await db.PostCategories.Where(e => categories.Contains(e.Slug)).ToListAsync();
            if (post.Categories.Count != categories.Distinct().Count())
                throw new InvalidOperationException("Categories contains one or more categories that wasn't exists.");
        }
        
        if (post.Empty)
            throw new InvalidOperationException("Cannot create a post with barely no content.");

        // TODO Notify the subscribers

        db.Posts.Add(post);
        await db.SaveChangesAsync();
        await fs.MarkUsageRangeAsync(post.Attachments, 1);

        return post;
    }

    public async Task<Post> UpdatePostAsync(
        Post post,
        List<string>? attachments = null,
        List<string>? tags = null,
        List<string>? categories = null
    )
    {
        post.EditedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

        if (attachments is not null)
        {
            var records = await db.Files.Where(e => attachments.Contains(e.Id)).ToListAsync();

            var previous = post.Attachments.ToDictionary(f => f.Id);
            var current = records.ToDictionary(f => f.Id);

            // Detect added files
            var added = current.Keys.Except(previous.Keys).Select(id => current[id]).ToList();
            // Detect removed files
            var removed = previous.Keys.Except(current.Keys).Select(id => previous[id]).ToList();

            // Update attachments
            post.Attachments = attachments.Select(id => current[id]).ToList();

            // Call mark usage
            await fs.MarkUsageRangeAsync(added, 1);
            await fs.MarkUsageRangeAsync(removed, -1);
        }
        
        if (tags is not null)
        {
            var existingTags = await db.PostTags.Where(e => tags.Contains(e.Slug)).ToListAsync();

            // Determine missing slugs
            var existingSlugs = existingTags.Select(t => t.Slug).ToHashSet();
            var missingSlugs = tags.Where(slug => !existingSlugs.Contains(slug)).ToList();

            var newTags = missingSlugs.Select(slug => new PostTag { Slug = slug }).ToList();
            if (newTags.Count > 0)
            {
                await db.PostTags.AddRangeAsync(newTags);
                await db.SaveChangesAsync();
            }

            post.Tags = existingTags.Concat(newTags).ToList();
        }

        if (categories is not null)
        {
            post.Categories = await db.PostCategories.Where(e => categories.Contains(e.Slug)).ToListAsync();
            if (post.Categories.Count != categories.Distinct().Count())
                throw new InvalidOperationException("Categories contains one or more categories that wasn't exists.");
        } 
        
        if (post.Empty)
            throw new InvalidOperationException("Cannot edit a post to barely no content.");
        
        db.Update(post);
        await db.SaveChangesAsync();
        
        return post;
    }

    public async Task DeletePostAsync(Post post)
    {
        db.Posts.Remove(post);
        await db.SaveChangesAsync();
        await fs.MarkUsageRangeAsync(post.Attachments, -1);
    }
}

public static class PostQueryExtensions
{
    public static IQueryable<Post> FilterWithVisibility(this IQueryable<Post> source, Account.Account? currentUser,
        bool isListing = false)
    {
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        source = isListing switch
        {
            true when currentUser is not null => source.Where(e =>
                e.Visibility != PostVisibility.Unlisted || e.Publisher.AccountId == currentUser.Id),
            true => source.Where(e => e.Visibility != PostVisibility.Unlisted),
            _ => source
        };

        if (currentUser is null)
            return source
                .Where(e => e.PublishedAt != null && now >= e.PublishedAt)
                .Where(e => e.Visibility == PostVisibility.Public);

        return source
            .Where(e => e.PublishedAt != null && now >= e.PublishedAt && e.Publisher.AccountId == currentUser.Id)
            .Where(e => e.Visibility != PostVisibility.Private || e.Publisher.AccountId == currentUser.Id);
    }
}