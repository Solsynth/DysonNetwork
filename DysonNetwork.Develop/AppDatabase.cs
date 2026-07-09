using DysonNetwork.Shared.Data;
using DysonNetwork.Develop.Models;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NodaTime;

namespace DysonNetwork.Develop;

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<SnDeveloper> Developers { get; set; } = null!;
    public DbSet<SnPublisher> Publishers { get; set; } = null!;

    public DbSet<SnDevProject> DevProjects { get; set; } = null!;
    
    public DbSet<SnCustomApp> CustomApps { get; set; } = null!;
    public DbSet<SnBoardWidget> BoardWidgets { get; set; } = null!;
    public DbSet<SnCustomAppSecret> CustomAppSecrets { get; set; } = null!;
    public DbSet<SnBotAccount> BotAccounts { get; set; } = null!;
    public DbSet<SnBotChatConfig> BotChatConfigs { get; set; } = null!;
    public DbSet<SnMiniApp> MiniApps { get; set; }
    public DbSet<SnAppProduct> AppProducts { get; set; }
    public DbSet<SnAppProductState> AppProductStates { get; set; }

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
    
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.ApplyAuditableAndSoftDelete();
        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SnAppProduct>()
            .HasOne(p => p.State)
            .WithOne(s => s.Product)
            .HasForeignKey<SnAppProductState>(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<SnBoardWidget>()
            .HasOne<SnCustomApp>()
            .WithMany(a => a.BoardWidgets)
            .HasForeignKey(w => w.AppId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnBoardWidget>()
            .HasIndex(w => new { w.AppId, w.Key })
            .IsUnique();

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
