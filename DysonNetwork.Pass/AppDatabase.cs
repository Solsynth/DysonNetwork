using System.Linq.Expressions;
using System.Reflection;
using DysonNetwork.Pass.Account;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Query;
using NodaTime;
using Quartz;

namespace DysonNetwork.Pass;

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
    public DbSet<AccountProfile> AccountProfiles { get; set; }
    public DbSet<AccountContact> AccountContacts { get; set; }
    public DbSet<AccountAuthFactor> AccountAuthFactors { get; set; }
    public DbSet<Relationship> AccountRelationships { get; set; }
    public DbSet<Status> AccountStatuses { get; set; }
    public DbSet<CheckInResult> AccountCheckInResults { get; set; }
    public DbSet<AccountBadge> Badges { get; set; }
    public DbSet<ActionLog> ActionLogs { get; set; }
    public DbSet<AbuseReport> AbuseReports { get; set; }

    public DbSet<AuthSession> AuthSessions { get; set; }
    public DbSet<AuthChallenge> AuthChallenges { get; set; }
    public DbSet<AuthDevice> AuthDevices { get; set; }
    
    public DbSet<Wallet.Wallet> Wallets { get; set; }
    public DbSet<WalletPocket> WalletPockets { get; set; }
    public DbSet<Order> PaymentOrders { get; set; }
    public DbSet<Transaction> PaymentTransactions { get; set; }
    public DbSet<Subscription> WalletSubscriptions { get; set; }
    public DbSet<Coupon> WalletCoupons { get; set; }
 
    public DbSet<Punishment> Punishments { get; set; }

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

        optionsBuilder.UseSeeding((context, _) => {});

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