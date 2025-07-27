using System.Linq.Expressions;
using System.Reflection;
using DysonNetwork.Drive.Billing;
using DysonNetwork.Drive.Storage;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Query;
using NodaTime;
using Quartz;

namespace DysonNetwork.Drive;

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<FilePool> Pools { get; set; } = null!;
    public DbSet<FileBundle> Bundles { get; set; } = null!;
    
    public DbSet<QuotaRecord> QuotaRecords { get; set; } = null!;
    
    public DbSet<CloudFile> Files { get; set; } = null!;
    public DbSet<CloudFileReference> FileReferences { get; set; } = null!;
    
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

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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