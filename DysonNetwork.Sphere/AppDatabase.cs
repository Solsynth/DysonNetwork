using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NodaTime;
using Npgsql;

namespace DysonNetwork.Sphere;

public abstract class BaseModel
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
    public DbSet<Account.Account> Accounts { get; set; }
    public DbSet<Account.AccountContact> AccountContacts { get; set; }
    public DbSet<Account.AccountAuthFactor> AccountAuthFactors { get; set; }
    public DbSet<Auth.Session> AuthSessions { get; set; }
    public DbSet<Auth.Challenge> AuthChallenges { get; set; }

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

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Automatically apply soft-delete filter to all entities inheriting BaseModel
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseModel).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(AppDatabase)
                    .GetMethod(nameof(SetSoftDeleteFilter),
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .MakeGenericMethod(entityType.ClrType);

                method.Invoke(null, [modelBuilder]);
            }
        }
    }

    private static void SetSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : BaseModel
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.DeletedAt == null);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        foreach (var entry in ChangeTracker.Entries<BaseModel>())
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