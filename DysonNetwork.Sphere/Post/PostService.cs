using System.Text.Json;
using DysonNetwork.Sphere.Activity;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class PostService(AppDatabase db, FileService fs, ActivityService act)
{
    public static List<Post> TruncatePostContent(List<Post> input)
    {
        // This truncate post content is designed for quill delta
        const int maxLength = 256;
        foreach (var item in input)
        {
            if (item.Content is not { RootElement: var rootElement }) continue;

            if (rootElement.ValueKind != JsonValueKind.Array) continue;
            var totalLength = 0;
            var truncatedArrayElements = new List<JsonElement>();

            foreach (var element in rootElement.EnumerateArray())
            {
                if (element is { ValueKind: JsonValueKind.Object } &&
                    element.TryGetProperty("insert", out var insertProperty))
                {
                    if (insertProperty is { ValueKind: JsonValueKind.String })
                    {
                        var textContent = insertProperty.GetString()!;
                        if (totalLength + textContent.Length <= maxLength)
                        {
                            truncatedArrayElements.Add(element);
                            totalLength += textContent.Length;
                        }
                        else
                        {
                            var remainingLength = maxLength - totalLength;
                            if (remainingLength > 0)
                            {
                                using var truncatedElementDocument =
                                    JsonDocument.Parse(
                                        $@"{{ ""insert"": ""{textContent.Substring(0, remainingLength)}"" }}"
                                    );
                                truncatedArrayElements.Add(truncatedElementDocument.RootElement.Clone());
                                totalLength = maxLength;
                            }

                            break;
                        }
                    }
                    else
                        truncatedArrayElements.Add(element);
                }
                else
                    truncatedArrayElements.Add(element);

                if (totalLength >= maxLength)
                    break;
            }

            using var newDocument = JsonDocument.Parse(JsonSerializer.Serialize(truncatedArrayElements));
            item.Content = newDocument;
        }

        return input;
    }

    public async Task<Post> PostAsync(
        Account.Account user,
        Post post,
        List<string>? attachments = null,
        List<string>? tags = null,
        List<string>? categories = null
    )
    {
        if (post.PublishedAt is not null)
        {
            if (post.PublishedAt.Value.ToDateTimeUtc() < DateTime.UtcNow)
                throw new InvalidOperationException("Cannot create the post which published in the past.");
        }
        else
        {
            post.PublishedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        }

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

        await act.CreateNewPostActivity(user, post);

        return post;
    }

    public async Task<Post> UpdatePostAsync(
        Post post,
        List<string>? attachments = null,
        List<string>? tags = null,
        List<string>? categories = null,
        Instant? publishedAt = null
    )
    {
        post.EditedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

        if (publishedAt is not null)
        {
            // User cannot set the published at to the past to prevent scam,
            // But we can just let the controller set the published at, because when no changes to
            // the published at will blocked the update operation
            if (publishedAt.Value.ToDateTimeUtc() < DateTime.UtcNow)
                throw new InvalidOperationException("Cannot set the published at to the past.");
        }

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
        List<long> userFriends, bool isListing = false)
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
            .Where(e => e.Visibility != PostVisibility.Private || e.Publisher.AccountId == currentUser.Id)
            .Where(e => e.Visibility != PostVisibility.Friends ||
                        (e.Publisher.AccountId != null && userFriends.Contains(e.Publisher.AccountId.Value)) ||
                        e.Publisher.AccountId == currentUser.Id);
    }
}