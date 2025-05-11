using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class PublisherService(AppDatabase db, FileService fs, IMemoryCache cache)
{
    public async Task<Publisher> CreateIndividualPublisher(
        Account.Account account,
        string? name,
        string? nick,
        string? bio,
        CloudFile? picture,
        CloudFile? background
    )
    {
        var publisher = new Publisher
        {
            PublisherType = PublisherType.Individual,
            Name = name ?? account.Name,
            Nick = nick ?? account.Nick,
            Bio = bio ?? account.Profile.Bio,
            Picture = picture ?? account.Profile.Picture,
            Background = background ?? account.Profile.Background,
            AccountId = account.Id,
            Members = new List<PublisherMember>
            {
                new()
                {
                    AccountId = account.Id,
                    Role = PublisherMemberRole.Owner,
                    JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        db.Publishers.Add(publisher);
        await db.SaveChangesAsync();

        if (publisher.Picture is not null) await fs.MarkUsageAsync(publisher.Picture, 1);
        if (publisher.Background is not null) await fs.MarkUsageAsync(publisher.Background, 1);

        return publisher;
    }

    // TODO Able to create organizational publisher when the realm system is completed

    public class PublisherStats
    {
        public int PostsCreated { get; set; }
        public int StickerPacksCreated { get; set; }
        public int StickersCreated { get; set; }
        public int UpvoteReceived { get; set; }
        public int DownvoteReceived { get; set; }
    }

    private const string PublisherStatsCacheKey = "PublisherStats_{0}";
    
    public async Task<PublisherStats?> GetPublisherStats(string name)
    {
        var cacheKey = string.Format(PublisherStatsCacheKey, name);
        if (cache.TryGetValue(cacheKey, out PublisherStats? stats))
            return stats;
    
        var publisher = await db.Publishers.FirstOrDefaultAsync(e => e.Name == name);
        if (publisher is null) return null;
        
        var postsCount = await db.Posts.Where(e => e.Publisher.Id == publisher.Id).CountAsync();
        var postsUpvotes = await db.PostReactions
            .Where(r => r.Post.Publisher.Id == publisher.Id && r.Attitude == PostReactionAttitude.Positive)
            .CountAsync();
        var postsDownvotes = await db.PostReactions
            .Where(r => r.Post.Publisher.Id == publisher.Id && r.Attitude == PostReactionAttitude.Negative)
            .CountAsync();
        
        var stickerPacksId = await db.StickerPacks.Where(e => e.Publisher.Id == publisher.Id).Select(e => e.Id).ToListAsync();
        var stickerPacksCount = stickerPacksId.Count;
    
        var stickersCount = await db.Stickers.Where(e => stickerPacksId.Contains(e.PackId)).CountAsync();
        
        stats = new PublisherStats
        {
            PostsCreated = postsCount,
            StickerPacksCreated = stickerPacksCount,
            StickersCreated = stickersCount,
            UpvoteReceived = postsUpvotes,
            DownvoteReceived = postsDownvotes
        };
    
        cache.Set(cacheKey, stats, TimeSpan.FromMinutes(5));
        return stats;
    }
}