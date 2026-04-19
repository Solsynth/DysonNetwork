using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.SnDoc;
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
    public DbSet<MiChanUserProfile> UserProfiles { get; set; }
    public DbSet<MiChanScheduledTask> ScheduledTasks { get; set; }
    public DbSet<MiChanInteractiveHistory> InteractiveHistories { get; set; }
    public DbSet<MiChanMoodState> MoodStates { get; set; }

    public DbSet<SnDocPage> SnDocPages { get; set; }
    public DbSet<SnDocChunk> SnDocChunks { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql(
                configuration.GetConnectionString("App"),
                opt => opt
                    .ConfigureDataSource(optSource => optSource.EnableDynamicJson())
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                    .UseNodaTime()
                    .UseVector()  // Enable pgvector support
            ).UseSnakeCaseNamingConvention();
        }

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

        // Add indexes on BotName columns for better query performance
        modelBuilder.Entity<SnThinkingSequence>()
            .HasIndex(s => s.BotName)
            .HasDatabaseName("ix_thinking_sequences_bot_name");

        modelBuilder.Entity<MiChanMemoryRecord>()
            .HasIndex(m => m.BotName)
            .HasDatabaseName("ix_memory_records_bot_name");

        modelBuilder.Entity<MiChanUserProfile>()
            .HasIndex(p => new { p.AccountId, p.BotName })
            .IsUnique()
            .HasDatabaseName("ix_user_profiles_account_id_bot_name");

        // SnDoc indexes
        modelBuilder.Entity<SnDocPage>()
            .HasIndex(p => p.Slug)
            .IsUnique()
            .HasDatabaseName("ix_sn_doc_pages_slug")
            .HasFilter("deleted_at IS NULL");

        modelBuilder.Entity<SnDocChunk>()
            .HasIndex(c => new { c.PageId, c.ChunkIndex })
            .HasDatabaseName("ix_sn_doc_chunks_page_id_chunk_index");

        modelBuilder.Entity<SnDocChunk>()
            .HasIndex(c => c.IsFirstChunk)
            .HasDatabaseName("ix_sn_doc_chunks_is_first_chunk");
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
