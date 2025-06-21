using System.Linq.Expressions;
using System.Reflection;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Chat;
using DysonNetwork.Sphere.Developer;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.Sticker;
using DysonNetwork.Sphere.Storage;
using DysonNetwork.Sphere.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Query;
using NodaTime;
using Npgsql;
using Quartz;

namespace DysonNetwork.Sphere;

public interface IIdentifiedResource
{
    public string ResourceIdentifier { get; }
}

public abstract class ModelBase
{
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }
    public Instant? DeletedAt { get; set; }
}

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<PermissionNode> PermissionNodes { get; set; }
    public DbSet<PermissionGroup> PermissionGroups { get; set; }
    public DbSet<PermissionGroupMember> PermissionGroupMembers { get; set; }

    public DbSet<MagicSpell> MagicSpells { get; set; }
    public DbSet<Account.Account> Accounts { get; set; }
    public DbSet<AccountConnection> AccountConnections { get; set; }
    public DbSet<Profile> AccountProfiles { get; set; }
    public DbSet<AccountContact> AccountContacts { get; set; }
    public DbSet<AccountAuthFactor> AccountAuthFactors { get; set; }
    public DbSet<Relationship> AccountRelationships { get; set; }
    public DbSet<Status> AccountStatuses { get; set; }
    public DbSet<CheckInResult> AccountCheckInResults { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<NotificationPushSubscription> NotificationPushSubscriptions { get; set; }
    public DbSet<Badge> Badges { get; set; }
    public DbSet<ActionLog> ActionLogs { get; set; }

    public DbSet<Session> AuthSessions { get; set; }
    public DbSet<Challenge> AuthChallenges { get; set; }

    public DbSet<CloudFile> Files { get; set; }
    public DbSet<CloudFileReference> FileReferences { get; set; }

    public DbSet<Publisher.Publisher> Publishers { get; set; }
    public DbSet<PublisherMember> PublisherMembers { get; set; }
    public DbSet<PublisherSubscription> PublisherSubscriptions { get; set; }
    public DbSet<PublisherFeature> PublisherFeatures { get; set; }

    public DbSet<Post.Post> Posts { get; set; }
    public DbSet<PostReaction> PostReactions { get; set; }
    public DbSet<PostTag> PostTags { get; set; }
    public DbSet<PostCategory> PostCategories { get; set; }
    public DbSet<PostCollection> PostCollections { get; set; }

    public DbSet<Realm.Realm> Realms { get; set; }
    public DbSet<RealmMember> RealmMembers { get; set; }

    public DbSet<ChatRoom> ChatRooms { get; set; }
    public DbSet<ChatMember> ChatMembers { get; set; }
    public DbSet<Message> ChatMessages { get; set; }
    public DbSet<RealtimeCall> ChatRealtimeCall { get; set; }
    public DbSet<MessageReaction> ChatReactions { get; set; }

    public DbSet<Sticker.Sticker> Stickers { get; set; }
    public DbSet<StickerPack> StickerPacks { get; set; }

    public DbSet<Wallet.Wallet> Wallets { get; set; }
    public DbSet<WalletPocket> WalletPockets { get; set; }
    public DbSet<Order> PaymentOrders { get; set; }
    public DbSet<Transaction> PaymentTransactions { get; set; }

    public DbSet<CustomApp> CustomApps { get; set; }
    public DbSet<CustomAppSecret> CustomAppSecrets { get; set; }

    public DbSet<Subscription> WalletSubscriptions { get; set; }
    public DbSet<Coupon> WalletCoupons { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(
            configuration.GetConnectionString("App"),
            opt => opt
                .ConfigureDataSource(optSource => optSource.EnableDynamicJson())
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                .UseNetTopologySuite()
                .UseNodaTime()
        ).UseSnakeCaseNamingConvention();

        optionsBuilder.UseAsyncSeeding(async (context, _, cancellationToken) =>
        {
            var defaultPermissionGroup = await context.Set<PermissionGroup>()
                .FirstOrDefaultAsync(g => g.Key == "default", cancellationToken);
            if (defaultPermissionGroup is null)
            {
                context.Set<PermissionGroup>().Add(new PermissionGroup
                {
                    Key = "default",
                    Nodes = new List<string>
                        {
                            "posts.create",
                            "posts.react",
                            "publishers.create",
                            "files.create",
                            "chat.create",
                            "chat.messages.create",
                            "chat.realtime.create",
                            "accounts.statuses.create",
                            "accounts.statuses.update",
                            "stickers.packs.create",
                            "stickers.create"
                        }.Select(permission =>
                            PermissionService.NewPermissionNode("group:default", "global", permission, true))
                        .ToList()
                });
                await context.SaveChangesAsync(cancellationToken);
            }
        });

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PermissionGroupMember>()
            .HasKey(pg => new { pg.GroupId, pg.Actor });
        modelBuilder.Entity<PermissionGroupMember>()
            .HasOne(pg => pg.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(pg => pg.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Relationship>()
            .HasKey(r => new { FromAccountId = r.AccountId, ToAccountId = r.RelatedId });
        modelBuilder.Entity<Relationship>()
            .HasOne(r => r.Account)
            .WithMany(a => a.OutgoingRelationships)
            .HasForeignKey(r => r.AccountId);
        modelBuilder.Entity<Relationship>()
            .HasOne(r => r.Related)
            .WithMany(a => a.IncomingRelationships)
            .HasForeignKey(r => r.RelatedId);

        modelBuilder.Entity<PublisherMember>()
            .HasKey(pm => new { pm.PublisherId, pm.AccountId });
        modelBuilder.Entity<PublisherMember>()
            .HasOne(pm => pm.Publisher)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.PublisherId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PublisherMember>()
            .HasOne(pm => pm.Account)
            .WithMany()
            .HasForeignKey(pm => pm.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PublisherSubscription>()
            .HasOne(ps => ps.Publisher)
            .WithMany(p => p.Subscriptions)
            .HasForeignKey(ps => ps.PublisherId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PublisherSubscription>()
            .HasOne(ps => ps.Account)
            .WithMany()
            .HasForeignKey(ps => ps.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Post.Post>()
            .HasGeneratedTsVectorColumn(p => p.SearchVector, "simple", p => new { p.Title, p.Description, p.Content })
            .HasIndex(p => p.SearchVector)
            .HasMethod("GIN");
        modelBuilder.Entity<Post.Post>()
            .HasOne(p => p.RepliedPost)
            .WithMany()
            .HasForeignKey(p => p.RepliedPostId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Post.Post>()
            .HasOne(p => p.ForwardedPost)
            .WithMany()
            .HasForeignKey(p => p.ForwardedPostId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Post.Post>()
            .HasMany(p => p.Tags)
            .WithMany(t => t.Posts)
            .UsingEntity(j => j.ToTable("post_tag_links"));
        modelBuilder.Entity<Post.Post>()
            .HasMany(p => p.Categories)
            .WithMany(c => c.Posts)
            .UsingEntity(j => j.ToTable("post_category_links"));
        modelBuilder.Entity<Post.Post>()
            .HasMany(p => p.Collections)
            .WithMany(c => c.Posts)
            .UsingEntity(j => j.ToTable("post_collection_links"));

        modelBuilder.Entity<RealmMember>()
            .HasKey(pm => new { pm.RealmId, pm.AccountId });
        modelBuilder.Entity<RealmMember>()
            .HasOne(pm => pm.Realm)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.RealmId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RealmMember>()
            .HasOne(pm => pm.Account)
            .WithMany()
            .HasForeignKey(pm => pm.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ChatMember>()
            .HasKey(pm => new { pm.Id });
        modelBuilder.Entity<ChatMember>()
            .HasAlternateKey(pm => new { pm.ChatRoomId, pm.AccountId });
        modelBuilder.Entity<ChatMember>()
            .HasOne(pm => pm.ChatRoom)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ChatMember>()
            .HasOne(pm => pm.Account)
            .WithMany()
            .HasForeignKey(pm => pm.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Message>()
            .HasOne(m => m.ForwardedMessage)
            .WithMany()
            .HasForeignKey(m => m.ForwardedMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Message>()
            .HasOne(m => m.RepliedMessage)
            .WithMany()
            .HasForeignKey(m => m.RepliedMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RealtimeCall>()
            .HasOne(m => m.Room)
            .WithMany()
            .HasForeignKey(m => m.RoomId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RealtimeCall>()
            .HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Cascade);

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

        logger.LogInformation("Cleaning up expired records...");

        // Expired relationships
        var affectedRows = await db.AccountRelationships
            .Where(x => x.ExpiredAt != null && x.ExpiredAt <= now)
            .ExecuteDeleteAsync();
        logger.LogDebug("Removed {Count} records of expired relationships.", affectedRows);
        // Expired permission group members
        affectedRows = await db.PermissionGroupMembers
            .Where(x => x.ExpiredAt != null && x.ExpiredAt <= now)
            .ExecuteDeleteAsync();
        logger.LogDebug("Removed {Count} records of expired permission group members.", affectedRows);

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