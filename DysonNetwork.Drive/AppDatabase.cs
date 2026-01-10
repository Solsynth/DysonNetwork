using System.Linq.Expressions;
using DysonNetwork.Drive.Billing;
using DysonNetwork.Drive.Storage;
using DysonNetwork.Drive.Storage.Model;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
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
    public DbSet<SnFileObject> FileObjects { get; set; } = null!;
    public DbSet<SnFileReplica> FileReplicas { get; set; } = null!;
    public DbSet<SnFilePermission> FilePermissions { get; set; } = null!;
    public DbSet<SnCloudFileIndex> FileIndexes { get; set; }

    public DbSet<PersistentTask> Tasks { get; set; } = null!;
    
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
