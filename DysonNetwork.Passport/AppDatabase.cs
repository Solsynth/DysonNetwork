using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Passport.Nfc;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NodaTime;
using Quartz;

namespace DysonNetwork.Passport;

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<SnPermissionNode> PermissionNodes { get; set; } = null!;
    public DbSet<SnPermissionGroup> PermissionGroups { get; set; } = null!;
    public DbSet<SnPermissionGroupMember> PermissionGroupMembers { get; set; } = null!;

    public DbSet<SnMagicSpell> MagicSpells { get; set; } = null!;
    public DbSet<SnAccountProfile> AccountProfiles { get; set; } = null!;
    public DbSet<SnAccountRelationship> AccountRelationships { get; set; } = null!;
    public DbSet<SnAccountStatus> AccountStatuses { get; set; } = null!;
    public DbSet<SnCheckInResult> AccountCheckInResults { get; set; } = null!;
    public DbSet<SnPresenceActivity> PresenceActivities { get; set; } = null!;
    public DbSet<SnAccountBadge> Badges { get; set; } = null!;
    
    public DbSet<SnRealm> Realms { get; set; } = null!;
    public DbSet<SnRealmMember> RealmMembers { get; set; } = null!;
    public DbSet<SnRealmLabel> RealmLabels { get; set; } = null!;
    public DbSet<SnRealmBoostContribution> RealmBoostContributions { get; set; } = null!;
    public DbSet<SnRealmExperienceRecord> RealmExperienceRecords { get; set; } = null!;

    public DbSet<SnSocialCreditRecord> SocialCreditRecords { get; set; } = null!;
    public DbSet<SnExperienceRecord> ExperienceRecords { get; set; } = null!;
    public DbSet<SnAchievementDefinition> AchievementDefinitions { get; set; } = null!;
    public DbSet<SnQuestDefinition> QuestDefinitions { get; set; } = null!;
    public DbSet<SnAccountAchievement> AccountAchievements { get; set; } = null!;
    public DbSet<SnAccountQuestProgress> AccountQuestProgresses { get; set; } = null!;
    public DbSet<SnProgressRewardGrant> ProgressRewardGrants { get; set; } = null!;
    public DbSet<SnProgressEventReceipt> ProgressEventReceipts { get; set; } = null!;

    public DbSet<SnAffiliationSpell> AffiliationSpells { get; set; } = null!;
    public DbSet<SnAffiliationResult> AffiliationResults { get; set; } = null!;
    
    public DbSet<SnRewindPoint> RewindPoints { get; set; }

    public DbSet<SnTicket> Tickets { get; set; } = null!;
    public DbSet<SnTicketMessage> TicketMessages { get; set; } = null!;
    public DbSet<SnMeet> Meets { get; set; } = null!;
    public DbSet<SnMeetParticipant> MeetParticipants { get; set; } = null!;
    public DbSet<SnNearbyDevice> NearbyDevices { get; set; } = null!;
    public DbSet<SnNearbyPresenceToken> NearbyPresenceTokens { get; set; } = null!;
    public DbSet<SnLocationPin> LocationPins { get; set; } = null!;

    public DbSet<SnNfcTag> NfcTags { get; set; } = null!;

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
                .UseNetTopologySuite()
                .UseNodaTime()
        ).UseSnakeCaseNamingConvention();

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Auth/account/E2EE moved to Padlock; keep these entities out of Passport EF model.
        modelBuilder.Ignore<SnAccount>();
        modelBuilder.Ignore<SnAccountAuthFactor>();
        modelBuilder.Ignore<SnAccountConnection>();
        modelBuilder.Ignore<SnAccountContact>();
        modelBuilder.Ignore<SnAuthChallenge>();
        modelBuilder.Ignore<SnAuthClient>();
        modelBuilder.Ignore<SnAuthSession>();
        modelBuilder.Ignore<SnE2eeDevice>();
        modelBuilder.Ignore<SnE2eeEnvelope>();
        modelBuilder.Ignore<SnE2eeKeyBundle>();
        modelBuilder.Ignore<SnE2eeOneTimePreKey>();
        modelBuilder.Ignore<SnE2eeSession>();
        modelBuilder.Ignore<SnMlsKeyPackage>();
        modelBuilder.Ignore<SnMlsGroupState>();
        modelBuilder.Ignore<SnMlsDeviceMembership>();
        modelBuilder.Ignore<SnSubscriptionReferenceObject>();

        modelBuilder.Entity<SnPermissionGroupMember>()
            .HasKey(pg => new { pg.GroupId, pg.Actor });
        modelBuilder.Entity<SnPermissionGroupMember>()
            .HasOne(pg => pg.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(pg => pg.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnAccountRelationship>()
            .HasKey(r => new { FromAccountId = r.AccountId, ToAccountId = r.RelatedId });
        
        modelBuilder.Entity<SnRealmMember>()
            .HasKey(pm => new { pm.RealmId, pm.AccountId });
        modelBuilder.Entity<SnRealmMember>()
            .HasOne(pm => pm.Realm)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.RealmId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnRealmMember>()
            .HasOne(pm => pm.Label)
            .WithMany()
            .HasForeignKey(pm => pm.LabelId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<SnRealmLabel>()
            .HasOne(l => l.Realm)
            .WithMany(r => r.Labels)
            .HasForeignKey(l => l.RealmId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnMeetParticipant>()
            .HasKey(p => new { p.MeetId, p.AccountId });
        modelBuilder.Entity<SnMeetParticipant>()
            .HasOne(p => p.Meet)
            .WithMany(m => m.Participants)
            .HasForeignKey(p => p.MeetId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnNearbyPresenceToken>()
            .HasOne(t => t.Device)
            .WithMany(d => d.PresenceTokens)
            .HasForeignKey(t => t.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Passport no longer owns auth/account rows; keep profile as an account-id keyed read model only.
        modelBuilder.Entity<SnAccountProfile>()
            .Ignore(p => p.Account);

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

        logger.LogInformation("Cleaning up expired records...");

        // Expired relationships
        var affectedRows = await db.AccountRelationships
            .Where(x => x.ExpiredAt != null && x.ExpiredAt <= now)
            .ExecuteDeleteAsync();
        logger.LogDebug("Removed {Count} records of expired relationships.", affectedRows);
        // Expired permission group members
        affectedRows = await db.PermissionGroupMembers
            .Where(x => x.ExpiredAt != null && x.ExpiredAt <= now)
            .ExecuteDeleteAsync();
        logger.LogDebug("Removed {Count} records of expired permission group members.", affectedRows);
        affectedRows = await db.Meets
            .Where(x => x.Status == MeetStatus.Active && x.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MeetStatus.Expired)
                .SetProperty(m => m.UpdatedAt, now));
        logger.LogDebug("Expired {Count} stale meets.", affectedRows);
        affectedRows = await db.NearbyPresenceTokens
            .Where(x => x.ValidTo <= now - Duration.FromMinutes(5))
            .ExecuteDeleteAsync();
        logger.LogDebug("Removed {Count} expired nearby presence tokens.", affectedRows);

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
