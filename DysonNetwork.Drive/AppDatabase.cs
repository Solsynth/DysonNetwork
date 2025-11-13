using System.Linq.Expressions;
using System.Reflection;
using DysonNetwork.Drive.Billing;
using DysonNetwork.Drive.Storage;
using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Query;
using NodaTime;
using Quartz;
using TaskStatus = DysonNetwork.Drive.Storage.Model.TaskStatus;

namespace DysonNetwork.Drive;

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<FilePool> Pools { get; set; } = null!;
    public DbSet<SnFileBundle> Bundles { get; set; } = null!;
    
    public DbSet<QuotaRecord> QuotaRecords { get; set; } = null!;
    
    public DbSet<SnCloudFile> Files { get; set; } = null!;
    public DbSet<CloudFileReference> FileReferences { get; set; } = null!;
    public DbSet<SnCloudFileIndex> FileIndexes { get; set; }
    public DbSet<SnCloudFolder> Folders { get; set; } = null!;

    public DbSet<PersistentTask> Tasks { get; set; } = null!;
    public DbSet<PersistentUploadTask> UploadTasks { get; set; } = null!; // Backward compatibility
    
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

        // Apply soft-delete filter only to root entities, not derived types
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ModelBase).IsAssignableFrom(entityType.ClrType)) continue;

            // Skip derived types to avoid filter conflicts
            var clrType = entityType.ClrType;
            if (clrType.BaseType != typeof(object) &&
                typeof(ModelBase).IsAssignableFrom(clrType.BaseType))
            {
                continue; // Skip derived types
            }

            var method = typeof(AppDatabase)
                .GetMethod(nameof(SetSoftDeleteFilter),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(clrType);

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

public class PersistentTaskCleanupJob(
    IServiceProvider serviceProvider,
    ILogger<PersistentTaskCleanupJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Cleaning up stale persistent tasks...");

        // Get the PersistentTaskService from DI
        using var scope = serviceProvider.CreateScope();
        var persistentTaskService = scope.ServiceProvider.GetService(typeof(PersistentTaskService));

        if (persistentTaskService is PersistentTaskService service)
        {
            // Clean up tasks for all users (you might want to add user-specific logic here)
            // For now, we'll clean up tasks older than 30 days for all users
            var cutoff = SystemClock.Instance.GetCurrentInstant() - Duration.FromDays(30);
            var tasksToClean = await service.GetUserTasksAsync(
                Guid.Empty, // This would need to be adjusted for multi-user cleanup
                status: TaskStatus.Completed | TaskStatus.Failed | TaskStatus.Cancelled | TaskStatus.Expired
            );

            var cleanedCount = 0;
            foreach (var task in tasksToClean.Items.Where(t => t.UpdatedAt < cutoff))
            {
                await service.CancelTaskAsync(task.TaskId); // Or implement a proper cleanup method
                cleanedCount++;
            }

            logger.LogInformation("Cleaned up {Count} stale persistent tasks", cleanedCount);
        }
        else
        {
            logger.LogWarning("PersistentTaskService not found in DI container");
        }
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
