using System.Linq.Expressions;
using DysonNetwork.Sphere.Permission;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NodaTime;
using Npgsql;
using Quartz;

namespace DysonNetwork.Sphere;

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

    public DbSet<Account.MagicSpell> MagicSpells { get; set; }
    public DbSet<Account.Account> Accounts { get; set; }
    public DbSet<Account.Profile> AccountProfiles { get; set; }
    public DbSet<Account.AccountContact> AccountContacts { get; set; }
    public DbSet<Account.AccountAuthFactor> AccountAuthFactors { get; set; }
    public DbSet<Account.Relationship> AccountRelationships { get; set; }
    public DbSet<Account.Notification> Notifications { get; set; }
    public DbSet<Account.NotificationPushSubscription> NotificationPushSubscriptions { get; set; }

    public DbSet<Auth.Session> AuthSessions { get; set; }
    public DbSet<Auth.Challenge> AuthChallenges { get; set; }

    public DbSet<Storage.CloudFile> Files { get; set; }

    public DbSet<Activity.Activity> Activities { get; set; }

    public DbSet<Post.Publisher> Publishers { get; set; }
    public DbSet<Post.PublisherMember> PublisherMembers { get; set; }
    public DbSet<Post.Post> Posts { get; set; }
    public DbSet<Post.PostReaction> PostReactions { get; set; }
    public DbSet<Post.PostTag> PostTags { get; set; }
    public DbSet<Post.PostCategory> PostCategories { get; set; }
    public DbSet<Post.PostCollection> PostCollections { get; set; }

    public DbSet<Realm.Realm> Realms { get; set; }
    public DbSet<Realm.RealmMember> RealmMembers { get; set; }

    public DbSet<Chat.ChatRoom> ChatRooms { get; set; }
    public DbSet<Chat.ChatMember> ChatMembers { get; set; }
    public DbSet<Chat.Message> ChatMessages { get; set; }
    public DbSet<Chat.MessageStatus> ChatStatuses { get; set; }
    public DbSet<Chat.MessageReaction> ChatReactions { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(configuration.GetConnectionString("App"));
        dataSourceBuilder.EnableDynamicJson();
        dataSourceBuilder.UseNodaTime();
        var dataSource = dataSourceBuilder.Build();

        optionsBuilder.UseNpgsql(
            dataSource,
            opt => opt.UseNodaTime()
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
                    Nodes =
                    {
                        PermissionService.NewPermissionNode("group:default", "global", "posts.create", true),
                        PermissionService.NewPermissionNode("group:default", "global", "posts.react", true),
                        PermissionService.NewPermissionNode("group:default", "global", "publishers.create", true),
                        PermissionService.NewPermissionNode("group:default", "global", "files.create", true),
                        PermissionService.NewPermissionNode("group:default", "global", "chat.create", true)
                    }
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

        modelBuilder.Entity<Account.Account>()
            .HasOne(a => a.Profile)
            .WithOne(p => p.Account)
            .HasForeignKey<Account.Profile>(p => p.Id);

        modelBuilder.Entity<Account.Relationship>()
            .HasKey(r => new { FromAccountId = r.AccountId, ToAccountId = r.RelatedId });
        modelBuilder.Entity<Account.Relationship>()
            .HasOne(r => r.Account)
            .WithMany(a => a.OutgoingRelationships)
            .HasForeignKey(r => r.AccountId);
        modelBuilder.Entity<Account.Relationship>()
            .HasOne(r => r.Related)
            .WithMany(a => a.IncomingRelationships)
            .HasForeignKey(r => r.RelatedId);

        modelBuilder.Entity<Post.PublisherMember>()
            .HasKey(pm => new { pm.PublisherId, pm.AccountId });
        modelBuilder.Entity<Post.PublisherMember>()
            .HasOne(pm => pm.Publisher)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.PublisherId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Post.PublisherMember>()
            .HasOne(pm => pm.Account)
            .WithMany()
            .HasForeignKey(pm => pm.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Post.Post>()
            .HasGeneratedTsVectorColumn(p => p.SearchVector, "simple", p => new { p.Title, p.Description, p.Content })
            .HasIndex(p => p.SearchVector)
            .HasMethod("GIN");
        modelBuilder.Entity<Post.Post>()
            .HasOne(p => p.ThreadedPost)
            .WithOne()
            .HasForeignKey<Post.Post>(p => p.ThreadedPostId);
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

        modelBuilder.Entity<Realm.RealmMember>()
            .HasKey(pm => new { pm.RealmId, pm.AccountId });
        modelBuilder.Entity<Realm.RealmMember>()
            .HasOne(pm => pm.Realm)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.RealmId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Realm.RealmMember>()
            .HasOne(pm => pm.Account)
            .WithMany()
            .HasForeignKey(pm => pm.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Chat.ChatMember>()
            .HasKey(pm => new { pm.Id });
        modelBuilder.Entity<Chat.ChatMember>()
            .HasAlternateKey(pm => new { pm.ChatRoomId, pm.AccountId });
        modelBuilder.Entity<Chat.ChatMember>()
            .HasOne(pm => pm.ChatRoom)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Chat.ChatMember>()
            .HasOne(pm => pm.Account)
            .WithMany()
            .HasForeignKey(pm => pm.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Chat.MessageStatus>()
            .HasKey(e => new { e.MessageId, e.SenderId });
        modelBuilder.Entity<Chat.Message>()
            .HasOne(m => m.ForwardedMessage)
            .WithMany()
            .HasForeignKey(m => m.ForwardedMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Chat.Message>()
            .HasOne(m => m.RepliedMessage)
            .WithMany()
            .HasForeignKey(m => m.RepliedMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        // Automatically apply soft-delete filter to all entities inheriting BaseModel
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ModelBase).IsAssignableFrom(entityType.ClrType)) continue;
            var method = typeof(AppDatabase)
                .GetMethod(nameof(SetSoftDeleteFilter),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
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
        logger.LogInformation("Deleting soft-deleted records...");

        var now = SystemClock.Instance.GetCurrentInstant();
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