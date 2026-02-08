using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NodaTime;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DysonNetwork.Insight;

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<SnThinkingSequence> ThinkingSequences { get; set; }
    public DbSet<SnThinkingThought> ThinkingThoughts { get; set; }
    public DbSet<SnUnpaidAccount> UnpaidAccounts { get; set; }
    
    public DbSet<SnWebArticle> WebArticles { get; set; }
    public DbSet<SnWebFeed> WebFeeds { get; set; }
    public DbSet<SnWebFeedSubscription> WebFeedSubscriptions { get; set; }

    public DbSet<MiChan.MiChanInteraction> MiChanInteractions { get; set; }
    
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
        
        modelBuilder.Ignore<SnAccount>();
        
        modelBuilder.ApplySoftDeleteFilters();
        
        // Configure pgvector extension
        modelBuilder.HasPostgresExtension("vector");
        
        // Configure MiChanInteraction
        modelBuilder.Entity<MiChan.MiChanInteraction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ContextId);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.CreatedAt);
            
            // Configure the embedding column with proper type (qwen/qwen3-embedding-8b uses 4096 dimensions)
            entity.Property(e => e.Embedding)
                .HasColumnType("vector(4096)");
            
            // Create HNSW index for vector similarity search (fast approximate nearest neighbor)
            entity.HasIndex(e => e.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });
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
