using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Padlock.Permission;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DysonNetwork.Padlock;

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<SnPermissionNode> PermissionNodes { get; set; } = null!;
    public DbSet<SnPermissionGroup> PermissionGroups { get; set; } = null!;
    public DbSet<SnPermissionGroupMember> PermissionGroupMembers { get; set; } = null!;

    public DbSet<SnAccount> Accounts { get; set; } = null!;
    public DbSet<SnAccountConnection> AccountConnections { get; set; } = null!;
    public DbSet<SnAccountContact> AccountContacts { get; set; } = null!;
    public DbSet<SnAccountAuthFactor> AccountAuthFactors { get; set; } = null!;

    public DbSet<SnAuthSession> AuthSessions { get; set; } = null!;
    public DbSet<SnAuthChallenge> AuthChallenges { get; set; } = null!;
    public DbSet<SnAuthClient> AuthClients { get; set; } = null!;
    public DbSet<SnApiKey> ApiKeys { get; set; } = null!;

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
                            "auth.login",
                            "auth.logout"
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

        // Padlock keeps auth/security ownership; relationship graph lives in Pass.
        modelBuilder.Ignore<SnAccountProfile>();
        modelBuilder.Ignore<SnAccountRelationship>();
        modelBuilder.Entity<SnAccount>().Ignore(a => a.Profile);
        modelBuilder.Entity<SnAccount>().Ignore(a => a.IncomingRelationships);
        modelBuilder.Entity<SnAccount>().Ignore(a => a.OutgoingRelationships);

        modelBuilder.Entity<SnPermissionGroupMember>()
            .HasKey(pg => new { pg.GroupId, pg.Actor });
        modelBuilder.Entity<SnPermissionGroupMember>()
            .HasOne(pg => pg.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(pg => pg.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.ApplySoftDeleteFilters();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.ApplyAuditableAndSoftDelete();
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
