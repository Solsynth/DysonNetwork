using System.Linq.Expressions;
using System.Data;
using DysonNetwork.Messager.Models;
using DysonNetwork.Messager.Chat.Voice;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using Quartz;

namespace DysonNetwork.Messager;

public class AppDatabase(
    DbContextOptions<AppDatabase> options,
    IConfiguration configuration
) : DbContext(options)
{
    public DbSet<SnChatRoom> ChatRooms { get; set; } = null!;
    public DbSet<SnChatMember> ChatMembers { get; set; } = null!;
    public DbSet<SnChatGroup> ChatGroups { get; set; } = null!;
    public DbSet<SnChatMessage> ChatMessages { get; set; } = null!;
    public DbSet<SnRealtimeCall> ChatRealtimeCall { get; set; } = null!;
    public DbSet<SnChatReaction> ChatReactions { get; set; } = null!;
    public DbSet<SnChatVoiceClip> ChatVoiceClips { get; set; } = null!;
    public DbSet<SnChatMessagePin> ChatMessagePins { get; set; } = null!;
    public DbSet<SnChatRoomCounter> ChatRoomCounters { get; set; } = null!;



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

        modelBuilder.Entity<SnChatMember>()
            .HasKey(pm => new { pm.Id });
        modelBuilder.Entity<SnChatMember>()
            .HasAlternateKey(pm => new { pm.ChatRoomId, pm.AccountId });
        modelBuilder.Entity<SnChatMember>()
            .HasOne(pm => pm.ChatRoom)
            .WithMany(p => p.Members)
            .HasForeignKey(pm => pm.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnChatMessage>()
            .HasOne(m => m.ForwardedMessage)
            .WithMany()
            .HasForeignKey(m => m.ForwardedMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SnChatMessage>()
            .HasOne(m => m.RepliedMessage)
            .WithMany()
            .HasForeignKey(m => m.RepliedMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SnChatMessage>()
            .HasIndex(m => new { m.ChatRoomId, m.IsEncrypted, m.CreatedAt });
        modelBuilder.Entity<SnChatMessage>()
            .HasIndex(m => new { m.ChatRoomId, m.RoomSequence })
            .IsUnique();
        modelBuilder.Entity<SnChatMessage>()
            .HasIndex(m => new { m.ChatRoomId, m.SenderId, m.ClientMessageId })
            .HasFilter("client_message_id IS NOT NULL");

        // Partial unique index: max 1 active placeholder per member per room
        modelBuilder.Entity<SnChatMessage>()
            .HasIndex(m => new { m.ChatRoomId, m.SenderId })
            .IsUnique()
            .HasFilter("type = 'placeholder' AND deleted_at IS NULL");
        modelBuilder.Entity<SnRealtimeCall>()
            .HasOne(m => m.Room)
            .WithMany()
            .HasForeignKey(m => m.RoomId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnRealtimeCall>()
            .HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnChatVoiceClip>()
            .HasOne(v => v.ChatRoom)
            .WithMany()
            .HasForeignKey(v => v.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnChatVoiceClip>()
            .HasOne(v => v.Sender)
            .WithMany()
            .HasForeignKey(v => v.SenderId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnChatMessagePin>()
            .HasOne(p => p.Message)
            .WithMany()
            .HasForeignKey(p => p.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SnChatMessagePin>()
            .HasOne(p => p.PinnedBy)
            .WithMany()
            .HasForeignKey(p => p.PinnedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SnChatMessagePin>()
            .HasIndex(p => new { p.ChatRoomId, p.MessageId })
            .IsUnique();
        modelBuilder.Entity<SnChatMessagePin>()
            .HasIndex(p => new { p.ChatRoomId, p.ExpiresAt });
        modelBuilder.Entity<SnChatRoomCounter>()
            .HasKey(c => c.ChatRoomId);

        modelBuilder.Entity<SnChatGroup>()
            .HasIndex(g => new { g.AccountId, g.Name });
        modelBuilder.Entity<SnChatMember>()
            .HasOne(m => m.ChatGroup)
            .WithMany()
            .HasForeignKey(m => m.ChatGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.ApplySoftDeleteFilters();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.ApplyAuditableAndSoftDelete();

        var addedChatMessages = ChangeTracker
            .Entries<SnChatMessage>()
            .Where(entry => entry.State == EntityState.Added && entry.Entity.RoomSequence <= 0)
            .ToList();

        if (addedChatMessages.Count == 0)
            return await base.SaveChangesAsync(cancellationToken);

        var startedTransaction = Database.CurrentTransaction is null;
        var transaction = Database.CurrentTransaction;
        if (startedTransaction)
            transaction = await Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await AssignChatRoomSequencesAsync(addedChatMessages, cancellationToken);

            var result = await base.SaveChangesAsync(cancellationToken);

            if (startedTransaction && transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return result;
        }
        catch
        {
            if (startedTransaction && transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (startedTransaction && transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task AssignChatRoomSequencesAsync(
        List<EntityEntry<SnChatMessage>> addedChatMessages,
        CancellationToken cancellationToken
    )
    {
        var connection = Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
            await connection.OpenAsync(cancellationToken);

        try
        {
        foreach (var roomGroup in addedChatMessages.GroupBy(entry => entry.Entity.ChatRoomId))
        {
            var roomMessages = roomGroup
                .OrderBy(entry => entry.Entity.CreatedAt)
                .ThenBy(entry => entry.Entity.Id)
                .ToList();

            var blockSize = roomMessages.Count;
            await using var command = connection.CreateCommand();
            command.Transaction = Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                INSERT INTO chat_room_counters (chat_room_id, last_sequence)
                VALUES (@chatRoomId, @blockSize)
                ON CONFLICT (chat_room_id)
                DO UPDATE SET last_sequence = chat_room_counters.last_sequence + @blockSize
                RETURNING last_sequence
                """;

            var chatRoomIdParameter = command.CreateParameter();
            chatRoomIdParameter.ParameterName = "@chatRoomId";
            chatRoomIdParameter.Value = roomGroup.Key;
            command.Parameters.Add(chatRoomIdParameter);

            var blockSizeParameter = command.CreateParameter();
            blockSizeParameter.ParameterName = "@blockSize";
            blockSizeParameter.Value = blockSize;
            command.Parameters.Add(blockSizeParameter);

            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            var lastSequence = Convert.ToInt64(scalar);

            var nextSequence = lastSequence - blockSize + 1;
            foreach (var messageEntry in roomMessages)
            {
                messageEntry.Entity.RoomSequence = nextSequence;
                nextSequence++;
            }
        }
        }
        finally
        {
            if (shouldCloseConnection)
                await connection.CloseAsync();
        }
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
