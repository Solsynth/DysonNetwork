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
    
    public DbSet<SnAccountPunishment> Punishments { get; set; } = null!;

    public DbSet<SnAuthSession> AuthSessions { get; set; } = null!;
    public DbSet<SnAuthChallenge> AuthChallenges { get; set; } = null!;
    public DbSet<SnAuthClient> AuthClients { get; set; } = null!;
    public DbSet<SnApiKey> ApiKeys { get; set; } = null!;
    public DbSet<SnAuthorizedApp> AuthorizedApps { get; set; } = null!;
    public DbSet<SnActionLog> ActionLogs { get; set; } = null!;
    public DbSet<SnE2eeKeyBundle> E2eeKeyBundles { get; set; } = null!;
    public DbSet<SnE2eeOneTimePreKey> E2eeOneTimePreKeys { get; set; } = null!;
    public DbSet<SnE2eeSession> E2eeSessions { get; set; } = null!;
    public DbSet<SnE2eeEnvelope> E2eeEnvelopes { get; set; } = null!;
    public DbSet<SnE2eeDevice> E2eeDevices { get; set; } = null!;
    public DbSet<SnMlsKeyPackage> MlsKeyPackages { get; set; } = null!;
    public DbSet<SnMlsGroupState> MlsGroupStates { get; set; } = null!;
    public DbSet<SnMlsDeviceMembership> MlsDeviceMemberships { get; set; } = null!;

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
                            "posts.create",
                            "posts.react",
                            "publishers.create",
                            "files.create",
                            "chat.create",
                            "chat.messages.create",
                            "chat.realtime.create",
                            "accounts.statuses.create",
                            "accounts.statuses.update",
                            "stickers.packs.create",
                            "stickers.create"
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
        modelBuilder.Ignore<SnAccountBadge>();
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

        modelBuilder.Entity<SnAuthorizedApp>()
            .HasIndex(a => new { a.AccountId, a.AppId, a.Type })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
        modelBuilder.Entity<SnAuthorizedApp>()
            .HasOne(a => a.Account)
            .WithMany()
            .HasForeignKey(a => a.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnAuthClient>()
            .HasIndex(c => new { c.AccountId, c.DeviceId })
            .IsUnique();
        modelBuilder.Entity<SnAuthClient>()
            .HasOne(c => c.Account)
            .WithMany()
            .HasForeignKey(c => c.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnE2eeDevice>()
            .HasIndex(d => new { d.AccountId, d.DeviceId })
            .IsUnique();
        modelBuilder.Entity<SnE2eeDevice>()
            .HasOne(d => d.Account)
            .WithMany()
            .HasForeignKey(d => d.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnE2eeKeyBundle>()
            .HasIndex(k => new { k.AccountId, k.DeviceId })
            .IsUnique();
        modelBuilder.Entity<SnE2eeKeyBundle>()
            .HasOne(k => k.Account)
            .WithMany()
            .HasForeignKey(k => k.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnE2eeOneTimePreKey>()
            .HasOne(k => k.KeyBundle)
            .WithMany(b => b.OneTimePreKeys)
            .HasForeignKey(k => k.KeyBundleId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnE2eeOneTimePreKey>()
            .HasIndex(k => new { k.KeyBundleId, k.KeyId })
            .IsUnique();
        modelBuilder.Entity<SnE2eeOneTimePreKey>()
            .HasIndex(k => new { k.KeyBundleId, k.IsClaimed });
        modelBuilder.Entity<SnE2eeOneTimePreKey>()
            .HasIndex(k => new { k.AccountId, k.DeviceId, k.IsClaimed });

        modelBuilder.Entity<SnE2eeSession>()
            .HasIndex(s => new { s.AccountAId, s.AccountBId })
            .IsUnique();

        modelBuilder.Entity<SnE2eeEnvelope>()
            .HasIndex(e => new { e.RecipientAccountId, e.RecipientDeviceId, e.DeliveryStatus, e.Sequence });
        modelBuilder.Entity<SnE2eeEnvelope>()
            .HasIndex(e => new
            {
                e.RecipientAccountId,
                e.RecipientDeviceId,
                e.SenderId,
                e.SenderDeviceId,
                e.ClientMessageId
            })
            .IsUnique()
            .HasFilter("client_message_id IS NOT NULL");
        modelBuilder.Entity<SnE2eeEnvelope>()
            .HasIndex(e => e.SessionId);

        modelBuilder.Entity<SnMlsKeyPackage>()
            .HasIndex(k => new { k.AccountId, k.DeviceId, k.IsConsumed });
        modelBuilder.Entity<SnMlsKeyPackage>()
            .HasOne(k => k.Account)
            .WithMany()
            .HasForeignKey(k => k.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnMlsGroupState>()
            .HasIndex(s => s.ChatRoomId)
            .IsUnique();
        modelBuilder.Entity<SnMlsGroupState>()
            .HasIndex(s => new { s.MlsGroupId, s.Epoch });

        modelBuilder.Entity<SnMlsDeviceMembership>()
            .HasIndex(m => new { m.ChatRoomId, m.AccountId, m.DeviceId })
            .IsUnique();
        modelBuilder.Entity<SnMlsDeviceMembership>()
            .HasIndex(m => new { m.MlsGroupId, m.LastSeenEpoch });

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
