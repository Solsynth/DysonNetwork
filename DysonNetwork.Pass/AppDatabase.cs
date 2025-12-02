using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
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
    public DbSet<SnPermissionNode> PermissionNodes { get; set; } = null!;
    public DbSet<SnPermissionGroup> PermissionGroups { get; set; } = null!;
    public DbSet<SnPermissionGroupMember> PermissionGroupMembers { get; set; } = null!;

    public DbSet<SnMagicSpell> MagicSpells { get; set; } = null!;
    public DbSet<SnAccount> Accounts { get; set; } = null!;
    public DbSet<SnAccountConnection> AccountConnections { get; set; } = null!;
    public DbSet<SnAccountProfile> AccountProfiles { get; set; } = null!;
    public DbSet<SnAccountContact> AccountContacts { get; set; } = null!;
    public DbSet<SnAccountAuthFactor> AccountAuthFactors { get; set; } = null!;
    public DbSet<SnAccountRelationship> AccountRelationships { get; set; } = null!;
    public DbSet<SnAccountStatus> AccountStatuses { get; set; } = null!;
    public DbSet<SnCheckInResult> AccountCheckInResults { get; set; } = null!;
    public DbSet<SnPresenceActivity> PresenceActivities { get; set; } = null!;
    public DbSet<SnAccountBadge> Badges { get; set; } = null!;
    public DbSet<SnActionLog> ActionLogs { get; set; } = null!;
    public DbSet<SnAbuseReport> AbuseReports { get; set; } = null!;

    public DbSet<SnAuthSession> AuthSessions { get; set; } = null!;
    public DbSet<SnAuthChallenge> AuthChallenges { get; set; } = null!;
    public DbSet<SnAuthClient> AuthClients { get; set; } = null!;
    public DbSet<SnApiKey> ApiKeys { get; set; } = null!;
    
    public DbSet<SnRealm> Realms { get; set; } = null!;
    public DbSet<SnRealmMember> RealmMembers { get; set; } = null!;

    public DbSet<SnWallet> Wallets { get; set; } = null!;
    public DbSet<SnWalletPocket> WalletPockets { get; set; } = null!;
    public DbSet<SnWalletOrder> PaymentOrders { get; set; } = null!;
    public DbSet<SnWalletTransaction> PaymentTransactions { get; set; } = null!;
    public DbSet<SnWalletFund> WalletFunds { get; set; } = null!;
    public DbSet<SnWalletFundRecipient> WalletFundRecipients { get; set; } = null!;
    public DbSet<SnWalletSubscription> WalletSubscriptions { get; set; } = null!;
    public DbSet<SnWalletGift> WalletGifts { get; set; } = null!;
    public DbSet<SnWalletCoupon> WalletCoupons { get; set; } = null!;

    public DbSet<SnAccountPunishment> Punishments { get; set; } = null!;

    public DbSet<SnSocialCreditRecord> SocialCreditRecords { get; set; } = null!;
    public DbSet<SnExperienceRecord> ExperienceRecords { get; set; } = null!;

    public DbSet<SnLottery> Lotteries { get; set; } = null!;
    public DbSet<SnLotteryRecord> LotteryRecords { get; set; } = null!;

    public DbSet<SnAffiliationSpell> AffiliationSpells { get; set; } = null!;
    public DbSet<SnAffiliationResult> AffiliationResults { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(
            configuration.GetConnectionString("App"),
            opt => opt
                .ConfigureDataSource(optSource => optSource
                    .EnableDynamicJson()
                    .ConfigureJsonOptions(new JsonSerializerOptions()
                    {
                        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                    })
                )
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                .UseNodaTime()
        ).UseSnakeCaseNamingConvention();

        optionsBuilder.UseAsyncSeeding(async (context, _, cancellationToken) =>
        {
            var defaultPermissionGroup = await context.Set<SnPermissionGroup>()
                .FirstOrDefaultAsync(g => g.Key == "default", cancellationToken);
            if (defaultPermissionGroup is null)
            {
                context.Set<SnPermissionGroup>().Add(new SnPermissionGroup
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
                            PermissionService.NewPermissionNode("group:default", permission, true))
                        .ToList()
                });
                await context.SaveChangesAsync(cancellationToken);
            }
        });

        optionsBuilder.UseSeeding((context, _) => { });

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SnPermissionGroupMember>()
            .HasKey(pg => new { pg.GroupId, pg.Actor });
        modelBuilder.Entity<SnPermissionGroupMember>()
            .HasOne(pg => pg.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(pg => pg.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnAccountRelationship>()
            .HasKey(r => new { FromAccountId = r.AccountId, ToAccountId = r.RelatedId });
        modelBuilder.Entity<SnAccountRelationship>()
            .HasOne(r => r.Account)
            .WithMany(a => a.OutgoingRelationships)
            .HasForeignKey(r => r.AccountId);
        modelBuilder.Entity<SnAccountRelationship>()
            .HasOne(r => r.Related)
            .WithMany(a => a.IncomingRelationships)
            .HasForeignKey(r => r.RelatedId);
        
        modelBuilder.Entity<SnRealmMember>()
            .HasKey(pm => new { pm.RealmId, pm.AccountId });
        modelBuilder.Entity<SnRealmMember>()
            .HasOne(pm => pm.Realm)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.RealmId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.ApplySoftDeleteFilters();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.ApplyAuditableAndSoftDelete();
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

