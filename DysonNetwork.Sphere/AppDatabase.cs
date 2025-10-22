using System.Linq.Expressions;
using System.Reflection;
using DysonNetwork.Shared.Models;
using DysonNetwork.Sphere.WebReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Query;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere;

public interface IIdentifiedResource
{
    public string ResourceIdentifier { get; }
}

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<SnPublisher> Publishers { get; set; } = null!;
    public DbSet<SnPublisherMember> PublisherMembers { get; set; } = null!;
    public DbSet<SnPublisherSubscription> PublisherSubscriptions { get; set; } = null!;
    public DbSet<SnPublisherFeature> PublisherFeatures { get; set; } = null!;

    public DbSet<SnPost> Posts { get; set; } = null!;
    public DbSet<SnPostReaction> PostReactions { get; set; } = null!;
    public DbSet<SnPostAward> PostAwards { get; set; } = null!;
    public DbSet<SnPostTag> PostTags { get; set; } = null!;
    public DbSet<SnPostCategory> PostCategories { get; set; } = null!;
    public DbSet<SnPostCollection> PostCollections { get; set; } = null!;
    public DbSet<SnPostFeaturedRecord> PostFeaturedRecords { get; set; } = null!;
    public DbSet<SnPostCategorySubscription> PostCategorySubscriptions { get; set; } = null!;

    public DbSet<SnPoll> Polls { get; set; } = null!;
    public DbSet<SnPollQuestion> PollQuestions { get; set; } = null!;
    public DbSet<SnPollAnswer> PollAnswers { get; set; } = null!;

    public DbSet<SnChatRoom> ChatRooms { get; set; } = null!;
    public DbSet<SnChatMember> ChatMembers { get; set; } = null!;
    public DbSet<SnChatMessage> ChatMessages { get; set; } = null!;
    public DbSet<SnRealtimeCall> ChatRealtimeCall { get; set; } = null!;
    public DbSet<SnChatMessageReaction> ChatReactions { get; set; } = null!;

    public DbSet<SnSticker> Stickers { get; set; } = null!;
    public DbSet<StickerPack> StickerPacks { get; set; } = null!;
    public DbSet<StickerPackOwnership> StickerPackOwnerships { get; set; } = null!;

    public DbSet<WebArticle> WebArticles { get; set; } = null!;
    public DbSet<WebFeed> WebFeeds { get; set; } = null!;
    public DbSet<WebFeedSubscription> WebFeedSubscriptions { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(
            configuration.GetConnectionString("App"),
            opt => opt
                .ConfigureDataSource(optSource => optSource.EnableDynamicJson())
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                .UseNodaTime()
        ).UseSnakeCaseNamingConvention();

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SnPublisherMember>()
            .HasKey(pm => new { pm.PublisherId, pm.AccountId });
        modelBuilder.Entity<SnPublisherMember>()
            .HasOne(pm => pm.Publisher)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.PublisherId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnPublisherSubscription>()
            .HasOne(ps => ps.Publisher)
            .WithMany(p => p.Subscriptions)
            .HasForeignKey(ps => ps.PublisherId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnPost>()
            .HasGeneratedTsVectorColumn(p => p.SearchVector, "simple", p => new { p.Title, p.Description, p.Content })
            .HasIndex(p => p.SearchVector)
            .HasMethod("GIN");

        modelBuilder.Entity<SnPost>()
            .HasOne(p => p.RepliedPost)
            .WithMany()
            .HasForeignKey(p => p.RepliedPostId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SnPost>()
            .HasOne(p => p.ForwardedPost)
            .WithMany()
            .HasForeignKey(p => p.ForwardedPostId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SnPost>()
            .HasMany(p => p.Tags)
            .WithMany(t => t.Posts)
            .UsingEntity(j => j.ToTable("post_tag_links"));
        modelBuilder.Entity<SnPost>()
            .HasMany(p => p.Categories)
            .WithMany(c => c.Posts)
            .UsingEntity(j => j.ToTable("post_category_links"));
        modelBuilder.Entity<SnPost>()
            .HasMany(p => p.Collections)
            .WithMany(c => c.Posts)
            .UsingEntity(j => j.ToTable("post_collection_links"));

        modelBuilder.Entity<SnChatMember>()
            .HasKey(pm => new { pm.Id });
        modelBuilder.Entity<SnChatMember>()
            .HasAlternateKey(pm => new { pm.ChatRoomId, pm.AccountId });
        modelBuilder.Entity<SnChatMember>()
            .HasOne(pm => pm.ChatRoom)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnChatMessage>()
            .HasOne(m => m.ForwardedMessage)
            .WithMany()
            .HasForeignKey(m => m.ForwardedMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SnChatMessage>()
            .HasOne(m => m.RepliedMessage)
            .WithMany()
            .HasForeignKey(m => m.RepliedMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SnRealtimeCall>()
            .HasOne(m => m.Room)
            .WithMany()
            .HasForeignKey(m => m.RoomId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnRealtimeCall>()
            .HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WebFeed>()
            .HasIndex(f => f.Url)
            .IsUnique();
        modelBuilder.Entity<WebArticle>()
            .HasIndex(a => a.Url)
            .IsUnique();

        // Automatically apply soft-delete filter to all entities inheriting BaseModel
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ModelBase).IsAssignableFrom(entityType.ClrType)) continue;
            var method = typeof(AppDatabase)
                .GetMethod(nameof(SetSoftDeleteFilter),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);

            method.Invoke(null, [modelBuilder]);
        }
    }

    private static void SetSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : ModelBase
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.DeletedAt == null);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        foreach (var entry in ChangeTracker.Entries<ModelBase>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.DeletedAt = now;
                    break;
                case EntityState.Detached:
                case EntityState.Unchanged:
                default:
                    break;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}

public class AppDatabaseRecyclingJob(AppDatabase db, ILogger<AppDatabaseRecyclingJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        logger.LogInformation("Deleting soft-deleted records...");

        var threshold = now - Duration.FromDays(7);

        var entityTypes = db.Model.GetEntityTypes()
            .Where(t => typeof(ModelBase).IsAssignableFrom(t.ClrType) && t.ClrType != typeof(ModelBase))
            .Select(t => t.ClrType);

        foreach (var entityType in entityTypes)
        {
            var set = (IQueryable)db.GetType().GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
                .MakeGenericMethod(entityType).Invoke(db, null)!;
            var parameter = Expression.Parameter(entityType, "e");
            var property = Expression.Property(parameter, nameof(ModelBase.DeletedAt));
            var condition = Expression.LessThan(property, Expression.Constant(threshold, typeof(Instant?)));
            var notNull = Expression.NotEqual(property, Expression.Constant(null, typeof(Instant?)));
            var finalCondition = Expression.AndAlso(notNull, condition);
            var lambda = Expression.Lambda(finalCondition, parameter);

            var queryable = set.Provider.CreateQuery(
                Expression.Call(
                    typeof(Queryable),
                    "Where",
                    [entityType],
                    set.Expression,
                    Expression.Quote(lambda)
                )
            );

            var toListAsync = typeof(EntityFrameworkQueryableExtensions)
                .GetMethod(nameof(EntityFrameworkQueryableExtensions.ToListAsync))!
                .MakeGenericMethod(entityType);

            var items = await (dynamic)toListAsync.Invoke(null, [queryable, CancellationToken.None])!;
            db.RemoveRange(items);
        }

        await db.SaveChangesAsync();
    }
}

public class AppDatabaseFactory : IDesignTimeDbContextFactory<AppDatabase>
{
    public AppDatabase CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<AppDatabase>();
        return new AppDatabase(optionsBuilder.Options, configuration);
    }
}

public static class OptionalQueryExtensions
{
    public static IQueryable<T> If<T>(
        this IQueryable<T> source,
        bool condition,
        Func<IQueryable<T>, IQueryable<T>> transform
    )
    {
        return condition ? transform(source) : source;
    }

    public static IQueryable<T> If<T, TP>(
        this IIncludableQueryable<T, TP> source,
        bool condition,
        Func<IIncludableQueryable<T, TP>, IQueryable<T>> transform
    )
        where T : class
    {
        return condition ? transform(source) : source;
    }

    public static IQueryable<T> If<T, TP>(
        this IIncludableQueryable<T, IEnumerable<TP>> source,
        bool condition,
        Func<IIncludableQueryable<T, IEnumerable<TP>>, IQueryable<T>> transform
    )
        where T : class
    {
        return condition ? transform(source) : source;
    }
}
