using DysonNetwork.Insight.MiChan;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DysonNetwork.Insight;

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<SnThinkingSequence> ThinkingSequences { get; set; }
    public DbSet<SnThinkingThought> ThinkingThoughts { get; set; }
    public DbSet<SnUnpaidAccount> UnpaidAccounts { get; set; }
    
    public DbSet<SnWebArticle> FeedArticles { get; set; }
    public DbSet<SnWebFeed> Feeds { get; set; }
    public DbSet<SnWebFeedSubscription> FeedSubscriptions { get; set; }
    
    public DbSet<MiChanMemoryRecord> MemoryRecords { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(
            configuration.GetConnectionString("App"),
            opt => opt
                .ConfigureDataSource(optSource => optSource.EnableDynamicJson())
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                .UseNodaTime()
                .UseVector()  // Enable pgvector support
        ).UseSnakeCaseNamingConvention();

        base.OnConfiguring(optionsBuilder);
    }
    
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.ApplyAuditableAndSoftDelete();
        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.HasPostgresExtension("vector");
        
        modelBuilder.ApplySoftDeleteFilters();
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
