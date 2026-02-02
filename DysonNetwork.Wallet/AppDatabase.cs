using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NodaTime;
using Quartz;

namespace DysonNetwork.Wallet;

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<SnWallet> Wallets { get; set; } = null!;
    public DbSet<SnWalletPocket> WalletPockets { get; set; } = null!;
    public DbSet<SnWalletOrder> PaymentOrders { get; set; } = null!;
    public DbSet<SnWalletTransaction> PaymentTransactions { get; set; } = null!;
    public DbSet<SnWalletFund> WalletFunds { get; set; } = null!;
    public DbSet<SnWalletFundRecipient> WalletFundRecipients { get; set; } = null!;
    public DbSet<SnWalletSubscription> WalletSubscriptions { get; set; } = null!;
    public DbSet<SnWalletGift> WalletGifts { get; set; } = null!;
    public DbSet<SnWalletCoupon> WalletCoupons { get; set; } = null!;

    public DbSet<SnLottery> Lotteries { get; set; } = null!;
    public DbSet<SnLotteryRecord> LotteryRecords { get; set; } = null!;

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

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ignore account-related entities that belong to Pass project
        // These are referenced via navigation properties but tables are in Pass database
        modelBuilder.Ignore<SnAccount>();
        modelBuilder.Ignore<SnAccountProfile>();
        modelBuilder.Ignore<SnPermissionGroupMember>();
        modelBuilder.Ignore<SnAccountRelationship>();
        modelBuilder.Ignore<SnRealmMember>();

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

