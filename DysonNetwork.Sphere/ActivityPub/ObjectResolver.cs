using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class ObjectResolver(
    AppDatabase db,
    ActivityPubDiscoveryService discoveryService,
    ILogger<ObjectResolver> logger,
    IConfiguration configuration
)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    public async Task<SnPost?> ResolveOrNullAsync(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;

        var post = await ResolveAsync(uri);
        return post;
    }

    public async Task<SnPost?> ResolveAsync(string uri)
    {
        logger.LogDebug("Resolving object: {Uri}", uri);

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
        {
            return await db.Posts.FirstOrDefaultAsync(p => p.FediverseUri == uri);
        }

        var domain = parsedUri.Host;
        if (domain == Domain)
        {
            return await ResolveLocalPostAsync(parsedUri);
        }

        return await ResolveRemotePostAsync(uri);
    }

    public async Task<SnPost?> ResolveByIdAsync(Guid id)
    {
        return await db.Posts
            .Include(p => p.Actor)
            .ThenInclude(a => a!.Instance)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<SnPost?> ResolveByFediverseUriAsync(string uri)
    {
        return await db.Posts
            .Include(p => p.Actor)
            .ThenInclude(a => a!.Instance)
            .FirstOrDefaultAsync(p => p.FediverseUri == uri);
    }

    public async Task<SnPost?> ResolveByWebfingerAsync(string account)
    {
        var parts = account.Split('@');
        if (parts.Length != 2)
            return null;

        var username = parts[0];
        var domain = parts[1];

        var actor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Username == username && a.Instance!.Domain == domain);

        if (actor == null)
            return null;

        var latestPost = await db.Posts
            .Where(p => p.ActorId == actor.Id)
            .OrderByDescending(p => p.PublishedAt)
            .FirstOrDefaultAsync();

        return latestPost;
    }

    public async Task<List<SnPost>> GetOutboxAsync(SnFediverseActor actor, int limit = 20, int offset = 0)
    {
        return await db.Posts
            .Where(p => p.ActorId == actor.Id)
            .Where(p => p.Visibility == PostVisibility.Public)
            .OrderByDescending(p => p.PublishedAt)
            .Skip(offset)
            .Take(limit)
            .Include(p => p.Attachments)
            .ToListAsync();
    }

    public async Task<int> GetOutboxCountAsync(SnFediverseActor actor)
    {
        return await db.Posts
            .Where(p => p.ActorId == actor.Id)
            .Where(p => p.Visibility == PostVisibility.Public)
            .CountAsync();
    }

    public async Task<List<SnPost>> GetContextAsync(SnPost post)
    {
        var context = new List<SnPost>();
        var visited = new HashSet<Guid>();

        var current = post;
        while (current.RepliedPostId.HasValue && !visited.Contains(current.Id))
        {
            visited.Add(current.Id);
            context.Insert(0, current);

            var parent = await db.Posts
                .Include(p => p.Actor)
                .FirstOrDefaultAsync(p => p.Id == current.RepliedPostId);

            if (parent == null)
                break;

            current = parent;
        }

        if (!visited.Contains(current.Id))
        {
            visited.Add(current.Id);
            context.Insert(0, current);
        }

        return context;
    }

    public async Task<List<SnPost>> GetRepliesAsync(SnPost post, int limit = 20)
    {
        return await db.Posts
            .Where(p => p.RepliedPostId == post.Id)
            .Where(p => p.Visibility == PostVisibility.Public)
            .OrderByDescending(p => p.PublishedAt)
            .Take(limit)
            .Include(p => p.Actor)
            .ToListAsync();
    }

    public async Task RefreshPostAsync(SnPost post)
    {
        if (string.IsNullOrEmpty(post.FediverseUri))
            return;

        var fetched = await discoveryService.FetchActivityAsync(post.FediverseUri, post.Actor?.Uri ?? "");
        if (fetched == null)
            return;

        await UpdatePostFromActivityAsync(post, fetched);
    }

    private async Task<SnPost?> ResolveLocalPostAsync(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/');
        var segments = path.Split('/');

        if (segments is not [.., "posts", var idStr])
            return null;

        if (!Guid.TryParse(idStr, out var id))
            return null;

        return await db.Posts
            .Include(p => p.Actor)
            .ThenInclude(a => a!.Instance)
            .Include(p => p.RepliedPost)
            .Include(p => p.ForwardedPost)
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    private async Task<SnPost?> ResolveRemotePostAsync(string uri)
    {
        var existing = await db.Posts
            .Include(p => p.Actor)
            .ThenInclude(a => a!.Instance)
            .FirstOrDefaultAsync(p => p.FediverseUri == uri);

        if (existing != null)
        {
            await db.SaveChangesAsync();
            return existing;
        }

        logger.LogInformation("Post not found locally, fetching: {Uri}", uri);
        return await FetchAndCreatePostAsync(uri);
    }

    private async Task<SnPost?> FetchAndCreatePostAsync(string postUri)
    {
        try
        {
            var uri = new Uri(postUri);
            var domain = uri.Host;

            var fetched = await discoveryService.FetchActivityAsync(postUri, "");
            if (fetched == null)
            {
                logger.LogWarning("Failed to fetch post: {Uri}", postUri);
                return null;
            }

            var objectData = GetObjectFromActivity(fetched);
            if (objectData == null)
            {
                logger.LogWarning("No object in activity: {Uri}", postUri);
                return null;
            }

            var objectType = GetString(objectData, "type");
            if (objectType != "Note" && objectType != "Article")
            {
                logger.LogInformation("Skipping non-note type: {Type}", objectType);
                return null;
            }

            var actorUri = GetString(objectData, "attributedTo") ?? GetString(fetched, "actor");
            if (string.IsNullOrEmpty(actorUri))
            {
                logger.LogWarning("No actor in post: {Uri}", postUri);
                return null;
            }

            var actor = await db.FediverseActors.FirstOrDefaultAsync(a => a.Uri == actorUri);

            var post = new SnPost
            {
                FediverseUri = GetString(objectData, "id") ?? postUri,
                FediverseType = objectType == "Article"
                    ? DyFediverseContentType.DyFediverseArticle
                    : DyFediverseContentType.DyFediverseNote,
                Title = GetString(objectData, "name"),
                Content = GetString(objectData, "content"),
                ContentType = PostContentType.Html,
                PublishedAt = ParseInstant(GetValue(objectData, "published")),
                EditedAt = ParseInstant(GetValue(objectData, "updated")),
                ActorId = actor?.Id,
                Type = objectType == "Article" ? PostType.Article : PostType.Moment,
                Visibility = PostVisibility.Public
            };

            var inReplyTo = GetString(objectData, "inReplyTo");
            if (!string.IsNullOrEmpty(inReplyTo))
            {
                var repliedTo = await ResolveAsync(inReplyTo);
                if (repliedTo != null)
                    post.RepliedPostId = repliedTo.Id;
            }

            db.Posts.Add(post);
            await db.SaveChangesAsync();

            logger.LogInformation("Fetched and created post: {Uri}", post.FediverseUri);
            return post;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching post: {Uri}", postUri);
            return null;
        }
    }

    private async Task UpdatePostFromActivityAsync(SnPost post, Dictionary<string, object> activity)
    {
        var objectData = GetObjectFromActivity(activity);
        if (objectData == null)
            return;

        post.Content = GetString(objectData, "content");
        post.EditedAt = ParseInstant(GetValue(objectData, "updated")) ?? SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();
    }

    private static Dictionary<string, object>? GetObjectFromActivity(Dictionary<string, object> activity)
    {
        var obj = activity.GetValueOrDefault("object");
        if (obj == null) return activity;

        if (obj is Dictionary<string, object> dict)
            return dict;

        if (obj is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var result = new Dictionary<string, object>();
            foreach (var prop in element.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => prop.Value.GetString() ?? "",
                    System.Text.Json.JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    _ => prop.Value.ToString()
                };
            }
            return result;
        }

        return activity;
    }

    private static string? GetString(Dictionary<string, object> dict, string key)
    {
        var value = dict.GetValueOrDefault(key);
        return value switch
        {
            string str => str,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString()
        };
    }

    private static object? GetValue(Dictionary<string, object> dict, string key)
    {
        return dict.GetValueOrDefault(key);
    }

    private static Instant? ParseInstant(object? value)
    {
        if (value == null) return null;

        if (value is Instant instant) return instant;

        if (DateTimeOffset.TryParse(value.ToString(), out var dto))
            return Instant.FromDateTimeOffset(dto);

        return null;
    }
}