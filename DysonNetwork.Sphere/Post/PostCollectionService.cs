using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class PostCollectionService(
    AppDatabase db,
    PostService postService,
    PublisherService publisherService,
    RemoteRealmService remoteRealmService
)
{
    public async Task<SnPostCollection?> GetCollectionBySlugAsync(string publisherName, string slug)
    {
        var normalizedSlug = NormalizeSlug(slug);
        return await db.PostCollections
            .Include(c => c.Publisher)
            .FirstOrDefaultAsync(c => c.Publisher.Name == publisherName && c.Slug == normalizedSlug);
    }

    public async Task<SnPostCollection> CreateCollectionAsync(
        SnPublisher publisher,
        string slug,
        string? name,
        string? description
    )
    {
        var normalizedSlug = NormalizeSlug(slug);
        var exists = await db.PostCollections.AnyAsync(c =>
            c.PublisherId == publisher.Id && c.Slug == normalizedSlug
        );
        if (exists)
            throw new InvalidOperationException("A collection with this slug already exists.");

        var collection = new SnPostCollection
        {
            PublisherId = publisher.Id,
            Publisher = publisher,
            Slug = normalizedSlug,
            Name = name,
            Description = description,
        };

        db.PostCollections.Add(collection);
        await db.SaveChangesAsync();
        return collection;
    }

    public async Task<SnPostCollection> UpdateCollectionAsync(
        SnPostCollection collection,
        string? name,
        string? description
    )
    {
        collection.Name = name;
        collection.Description = description;
        await db.SaveChangesAsync();
        return collection;
    }

    public async Task DeleteCollectionAsync(SnPostCollection collection)
    {
        db.PostCollections.Remove(collection);
        await db.SaveChangesAsync();
    }

    public async Task<List<SnPostCollection>> ListCollectionsAsync(string publisherName)
    {
        return await db.PostCollections
            .Include(c => c.Publisher)
            .Where(c => c.Publisher.Name == publisherName)
            .OrderBy(c => c.Name ?? c.Slug)
            .ThenBy(c => c.Slug)
            .ToListAsync();
    }

    public async Task<SnPostCollectionItem> AddPostAsync(SnPostCollection collection, Guid postId, int? order)
    {
        var post = await db.Posts
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null)
            throw new InvalidOperationException("Post not found.");

        var existing = await db.PostCollectionItems
            .FirstOrDefaultAsync(i => i.CollectionId == collection.Id && i.PostId == postId);
        if (existing is not null)
            throw new InvalidOperationException("Post already exists in this collection.");

        var maxOrder = await db.PostCollectionItems
            .Where(i => i.CollectionId == collection.Id)
            .Select(i => (int?)i.Order)
            .MaxAsync() ?? -1;

        var targetOrder = order ?? (maxOrder + 1);
        await ShiftOrdersAsync(collection.Id, targetOrder);

        var item = new SnPostCollectionItem
        {
            CollectionId = collection.Id,
            PostId = postId,
            Order = targetOrder,
        };

        db.PostCollectionItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    public async Task BatchAddPostsAsync(SnPostCollection collection, IReadOnlyList<Guid> postIds)
    {
        var existingIds = await db.PostCollectionItems
            .Where(i => i.CollectionId == collection.Id && postIds.Contains(i.PostId))
            .Select(i => i.PostId)
            .ToListAsync();

        var newIds = postIds.Except(existingIds).ToList();
        if (newIds.Count == 0)
            return;

        var existingPostIds = await db.Posts
            .Where(p => newIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();

        var missingIds = newIds.Except(existingPostIds).ToList();
        if (missingIds.Count > 0)
            throw new InvalidOperationException(
                $"Posts not found: {string.Join(", ", missingIds)}");

        var maxOrder = await db.PostCollectionItems
            .Where(i => i.CollectionId == collection.Id)
            .Select(i => (int?)i.Order)
            .MaxAsync() ?? -1;

        var items = newIds.Select((postId, index) => new SnPostCollectionItem
        {
            CollectionId = collection.Id,
            PostId = postId,
            Order = maxOrder + 1 + index,
        }).ToList();

        db.PostCollectionItems.AddRange(items);
        await db.SaveChangesAsync();
    }

    public async Task RemovePostAsync(SnPostCollection collection, Guid postId)
    {
        var item = await db.PostCollectionItems
            .FirstOrDefaultAsync(i => i.CollectionId == collection.Id && i.PostId == postId);
        if (item is null)
            return;

        var removedOrder = item.Order;
        db.PostCollectionItems.Remove(item);

        var trailingItems = await db.PostCollectionItems
            .Where(i => i.CollectionId == collection.Id && i.Order > removedOrder)
            .ToListAsync();
        foreach (var trailingItem in trailingItems)
            trailingItem.Order--;

        await db.SaveChangesAsync();
    }

    public async Task BatchRemovePostsAsync(SnPostCollection collection, IReadOnlyList<Guid> postIds)
    {
        var items = await db.PostCollectionItems
            .Where(i => i.CollectionId == collection.Id && postIds.Contains(i.PostId))
            .ToListAsync();
        if (items.Count == 0)
            return;

        db.PostCollectionItems.RemoveRange(items);

        var remainingItems = await db.PostCollectionItems
            .Where(i => i.CollectionId == collection.Id)
            .OrderBy(i => i.Order)
            .ToListAsync();
        for (var index = 0; index < remainingItems.Count; index++)
            remainingItems[index].Order = index;

        await db.SaveChangesAsync();
    }

    public async Task ReorderPostsAsync(SnPostCollection collection, IReadOnlyList<Guid> postIds)
    {
        var items = await db.PostCollectionItems
            .Where(i => i.CollectionId == collection.Id)
            .OrderBy(i => i.Order)
            .ToListAsync();

        var itemsByPostId = items.ToDictionary(i => i.PostId);
        if (itemsByPostId.Count != postIds.Count || postIds.Any(id => !itemsByPostId.ContainsKey(id)))
            throw new InvalidOperationException("Reorder payload must contain every post in the collection exactly once.");

        for (var index = 0; index < postIds.Count; index++)
            itemsByPostId[postIds[index]].Order = index;

        await db.SaveChangesAsync();
    }

    public async Task<List<SnPost>> ListPostsAsync(
        SnPostCollection collection,
        DyAccount? currentUser,
        List<Guid> userFriends,
        List<SnPublisher> userPublishers,
        int offset,
        int take
    )
    {
        var gatekeepInfo = await GetGatekeepInfoAsync(collection.PublisherId, currentUser);

        var query = db.PostCollectionItems
            .Where(i => i.CollectionId == collection.Id)
            .OrderBy(i => i.Order)
            .ThenBy(i => i.PostId)
            .Include(i => i.Post).ThenInclude(p => p.Publisher)
            .Include(i => i.Post).ThenInclude(p => p.Tags)
            .Include(i => i.Post).ThenInclude(p => p.Categories)
            .Include(i => i.Post).ThenInclude(p => p.RepliedPost)
            .Include(i => i.Post).ThenInclude(p => p.ForwardedPost)
            .Include(i => i.Post).ThenInclude(p => p.FeaturedRecords)
            .Select(i => i.Post)
            .FilterWithVisibility(
                currentUser,
                userFriends,
                userPublishers,
                isListing: true,
                gatekeepInfo.gatekeptPublisherIds,
                gatekeepInfo.subscriberPublisherIds
            );

        var posts = await query.Skip(offset).Take(take).ToListAsync();
        foreach (var post in posts)
        {
            if (post.RepliedPost != null)
                post.RepliedPost.RepliedPost = null;
        }

        posts = await postService.LoadPostInfo(posts, currentUser, true);
        await LoadPublisherCollectionsAsync(posts);
        await LoadPostsRealmsAsync(posts);
        return posts;
    }

    public async Task<int> CountVisiblePostsAsync(
        SnPostCollection collection,
        DyAccount? currentUser,
        List<Guid> userFriends,
        List<SnPublisher> userPublishers
    )
    {
        var gatekeepInfo = await GetGatekeepInfoAsync(collection.PublisherId, currentUser);

        return await db.PostCollectionItems
            .Where(i => i.CollectionId == collection.Id)
            .Select(i => i.Post)
            .FilterWithVisibility(
                currentUser,
                userFriends,
                userPublishers,
                isListing: true,
                gatekeepInfo.gatekeptPublisherIds,
                gatekeepInfo.subscriberPublisherIds
            )
            .CountAsync();
    }

    public async Task<SnPost?> GetAdjacentPostAsync(
        SnPostCollection collection,
        Guid postId,
        bool next,
        DyAccount? currentUser,
        List<Guid> userFriends,
        List<SnPublisher> userPublishers
    )
    {
        var gatekeepInfo = await GetGatekeepInfoAsync(collection.PublisherId, currentUser);

        var allItems = await db.PostCollectionItems
            .Where(i => i.CollectionId == collection.Id)
            .OrderBy(i => i.Order)
            .ThenBy(i => i.PostId)
            .Select(i => new { i.PostId, i.Order })
            .ToListAsync();

        var allPostIds = allItems.Select(i => i.PostId).ToList();

        var visiblePostIds = await db.Posts
            .Where(p => allPostIds.Contains(p.Id))
            .FilterWithVisibility(
                currentUser,
                userFriends,
                userPublishers,
                isListing: true,
                gatekeepInfo.gatekeptPublisherIds,
                gatekeepInfo.subscriberPublisherIds
            )
            .Select(p => p.Id)
            .ToListAsync();

        var visibleSet = visiblePostIds.ToHashSet();
        var orderedVisibleIds = allItems
            .Where(i => visibleSet.Contains(i.PostId))
            .Select(i => i.PostId)
            .ToList();

        var currentIndex = orderedVisibleIds.FindIndex(id => id == postId);
        if (currentIndex < 0)
            return null;

        var targetIndex = next ? currentIndex + 1 : currentIndex - 1;
        if (targetIndex < 0 || targetIndex >= orderedVisibleIds.Count)
            return null;

        var targetPostId = orderedVisibleIds[targetIndex];

        var post = await db.Posts
            .Where(p => p.Id == targetPostId)
            .Include(p => p.Publisher)
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .Include(p => p.RepliedPost)
            .Include(p => p.ForwardedPost)
            .Include(p => p.FeaturedRecords)
            .FirstOrDefaultAsync();
        if (post is null)
            return null;

        post = await postService.LoadPostInfo(post, currentUser, true);
        await LoadPublisherCollectionsAsync([post]);
        await LoadPostsRealmsAsync([post]);
        return post;
    }

    public async Task LoadPublisherCollectionsAsync(ICollection<SnPost> posts)
    {
        if (posts.Count == 0)
            return;

        var publisherPostGroups = posts
            .Where(p => p.PublisherId.HasValue)
            .GroupBy(p => p.PublisherId!.Value)
            .ToList();
        if (publisherPostGroups.Count == 0)
            return;

        var postIds = posts.Select(p => p.Id).ToList();
        var publisherIds = publisherPostGroups.Select(g => g.Key).ToList();

        var items = await db.PostCollectionItems
            .Where(i => postIds.Contains(i.PostId) && publisherIds.Contains(i.Collection.PublisherId))
            .Include(i => i.Collection)
            .ThenInclude(c => c.Publisher)
            .Select(i => new
            {
                i.PostId,
                Collection = i.Collection,
                i.Order,
            })
            .ToListAsync();

        var grouped = items
            .Join(
                posts.Where(p => p.PublisherId.HasValue),
                item => item.PostId,
                post => post.Id,
                (item, post) => new { item.PostId, item.Collection, item.Order, PostPublisherId = post.PublisherId!.Value }
            )
            .Where(x => x.Collection.PublisherId == x.PostPublisherId)
            .GroupBy(i => i.PostId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(i => i.Order)
                    .ThenBy(i => i.Collection.Slug)
                    .Select(i => i.Collection)
                    .DistinctBy(c => c.Id)
                    .ToList()
            );

        foreach (var post in posts)
            post.PublisherCollections = grouped.GetValueOrDefault(post.Id, []);
    }

    private async Task ShiftOrdersAsync(Guid collectionId, int fromOrder)
    {
        var items = await db.PostCollectionItems
            .Where(i => i.CollectionId == collectionId && i.Order >= fromOrder)
            .OrderByDescending(i => i.Order)
            .ToListAsync();
        foreach (var item in items)
            item.Order++;
    }

    private async Task<(HashSet<Guid>? gatekeptPublisherIds, HashSet<Guid>? subscriberPublisherIds)> GetGatekeepInfoAsync(
        Guid publisherId,
        DyAccount? currentUser
    )
    {
        var gatekeptPublisherIds = (await db.Publishers
            .Where(p => p.Id == publisherId && p.GatekeptFollows == true)
            .Select(p => p.Id)
            .ToListAsync()).ToHashSet();

        HashSet<Guid>? subscriberPublisherIds = null;
        if (gatekeptPublisherIds.Count > 0)
        {
            if (currentUser != null)
            {
                var currentAccountId = Guid.Parse(currentUser.Id);
                var activeSubscriptions = await db.PublisherSubscriptions
                    .Where(s => s.AccountId == currentAccountId && s.EndedAt == null && s.PublisherId == publisherId)
                    .Select(s => s.PublisherId)
                    .ToListAsync();
                subscriberPublisherIds = activeSubscriptions.ToHashSet();
            }
            else
            {
                subscriberPublisherIds = [];
            }
        }

        return (gatekeptPublisherIds.Count > 0 ? gatekeptPublisherIds : null, subscriberPublisherIds);
    }

    private async Task LoadPostsRealmsAsync(List<SnPost> posts)
    {
        var postRealmIds = posts
            .Where(p => p.RealmId != null)
            .Select(p => p.RealmId!.Value)
            .Distinct()
            .ToList();
        if (postRealmIds.Count == 0)
            return;

        var realms = await remoteRealmService.GetRealmBatch(postRealmIds.Select(id => id.ToString()).ToList());
        var realmDict = realms.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.FirstOrDefault());

        foreach (var post in posts.Where(p => p.RealmId != null))
        {
            if (realmDict.TryGetValue(post.RealmId!.Value, out var realm))
                post.Realm = realm;
        }
    }

    public static string NormalizeSlug(string value) => value.Trim().ToLowerInvariant();
}
