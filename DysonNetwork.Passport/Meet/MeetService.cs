using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NodaTime;

namespace DysonNetwork.Passport.Meet;

public class MeetService(
    AppDatabase db,
    AccountService accounts,
    DyFileService.DyFileServiceClient files,
    MeetSubscriptionHub subscriptions,
    MeetExpirationScheduler expirationScheduler,
    ILogger<MeetService> logger
)
{
    public static readonly Duration DefaultTtl = Duration.FromMinutes(30);
    public static readonly Duration MinimumTtl = Duration.FromMinutes(1);
    public static readonly Duration MaximumTtl = Duration.FromHours(24);
    public static readonly Duration DuplicateWindow = Duration.FromMinutes(5);

    public async Task<SnMeet> CreateMeetAsync(
        Guid hostId,
        MeetVisibility visibility,
        string? notes,
        string? imageId,
        string? locationName,
        string? locationAddress,
        Geometry? location,
        Dictionary<string, object>? metadata,
        Duration? ttl,
        CancellationToken cancellationToken = default
    )
    {
        await ExpireMeetsAsync(hostId, cancellationToken);

        var now = SystemClock.Instance.GetCurrentInstant();
        var normalizedTtl = NormalizeTtl(ttl);
        var existing = await db.Meets
            .Include(m => m.Participants)
            .Where(m => m.HostId == hostId)
            .Where(m => m.Status == MeetStatus.Active)
            .Where(m => m.ExpiresAt > now)
            .Where(m => m.CreatedAt >= now - DuplicateWindow)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            await HydrateMeetAsync(existing, cancellationToken);
            return existing;
        }

        var meet = new SnMeet
        {
            HostId = hostId,
            Status = MeetStatus.Active,
            Visibility = visibility,
            ExpiresAt = now + normalizedTtl,
            Notes = notes,
            Image = await LoadImageAsync(imageId),
            LocationName = locationName,
            LocationAddress = locationAddress,
            Location = location,
            Metadata = metadata ?? []
        };

        db.Meets.Add(meet);
        await db.SaveChangesAsync(cancellationToken);
        expirationScheduler.Schedule(meet.Id, meet.ExpiresAt);

        logger.LogInformation("Created meet {MeetId} for host {HostId}", meet.Id, hostId);

        await HydrateMeetAsync(meet, cancellationToken);
        return meet;
    }

    public async Task<List<SnMeet>> ListMeetsAsync(
        Guid accountId,
        MeetStatus? status = null,
        bool hostOnly = false,
        int offset = 0,
        int take = 20,
        CancellationToken cancellationToken = default
    )
    {
        await ExpireMeetsAsync(accountId, cancellationToken);

        var query = db.Meets
            .Include(m => m.Participants)
            .AsQueryable();

        if (hostOnly)
        {
            query = query.Where(m => m.HostId == accountId);
        }
        else
        {
            query = query.Where(m =>
                m.Visibility == MeetVisibility.Public
                || m.HostId == accountId
                || m.Participants.Any(p => p.AccountId == accountId));
        }

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        var meets = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync(cancellationToken);

        foreach (var meet in meets)
            await HydrateMeetAsync(meet, cancellationToken);

        return meets;
    }

    public async Task<SnMeet?> GetMeetAsync(Guid meetId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await TryExpireMeetAsync(meetId, cancellationToken);

        var meet = await db.Meets
            .Include(m => m.Participants)
            .FirstOrDefaultAsync(m => m.Id == meetId, cancellationToken);
        if (meet is null) return null;
        if (!CanAccessMeet(meet, accountId)) return null;

        await HydrateMeetAsync(meet, cancellationToken);
        return meet;
    }

    public async Task<SnMeet> JoinMeetAsync(Guid meetId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await TryExpireMeetAsync(meetId, cancellationToken);

        var meet = await db.Meets
            .Include(m => m.Participants)
            .FirstOrDefaultAsync(m => m.Id == meetId, cancellationToken)
            ?? throw new KeyNotFoundException("Meet not found.");

        if (meet.Status != MeetStatus.Active || meet.ExpiresAt <= SystemClock.Instance.GetCurrentInstant())
            throw new InvalidOperationException("Meet is no longer active.");

        var participantAdded = false;
        if (meet.HostId != accountId && meet.Participants.All(p => p.AccountId != accountId))
        {
            meet.Participants.Add(new SnMeetParticipant
            {
                MeetId = meet.Id,
                AccountId = accountId,
                JoinedAt = SystemClock.Instance.GetCurrentInstant()
            });
            participantAdded = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        await HydrateMeetAsync(meet, cancellationToken);

        if (participantAdded)
            await subscriptions.PublishAsync("participant_joined", meet, cancellationToken);

        return meet;
    }

    public async Task<SnMeet> CompleteMeetAsync(Guid meetId, Guid hostId, CancellationToken cancellationToken = default)
    {
        await TryExpireMeetAsync(meetId, cancellationToken);

        var meet = await db.Meets
            .Include(m => m.Participants)
            .FirstOrDefaultAsync(m => m.Id == meetId, cancellationToken)
            ?? throw new KeyNotFoundException("Meet not found.");

        if (meet.HostId != hostId)
            throw new UnauthorizedAccessException("Only the host can complete the meet.");
        if (meet.Status != MeetStatus.Active)
            throw new InvalidOperationException("Meet is already in a terminal state.");

        meet.Status = MeetStatus.Completed;
        meet.CompletedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);
        expirationScheduler.Cancel(meet.Id);

        await HydrateMeetAsync(meet, cancellationToken);
        await subscriptions.PublishAsync("completed", meet, cancellationToken);

        logger.LogInformation("Completed meet {MeetId} by host {HostId}", meetId, hostId);

        return meet;
    }

    public async Task<bool> TryExpireMeetAsync(Guid meetId, CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var meet = await db.Meets
            .Include(m => m.Participants)
            .FirstOrDefaultAsync(m => m.Id == meetId, cancellationToken);
        if (meet is null || meet.Status != MeetStatus.Active || meet.ExpiresAt > now)
            return false;

        meet.Status = MeetStatus.Expired;
        await db.SaveChangesAsync(cancellationToken);
        expirationScheduler.Cancel(meetId);

        await HydrateMeetAsync(meet, cancellationToken);
        await subscriptions.PublishAsync("expired", meet, cancellationToken);

        logger.LogInformation("Expired meet {MeetId}", meetId);

        return true;
    }

    public async Task ExpireMeetsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var meetIds = await db.Meets
            .Where(m => m.Status == MeetStatus.Active)
            .Where(m => m.ExpiresAt <= SystemClock.Instance.GetCurrentInstant())
            .Where(m => m.HostId == accountId || m.Participants.Any(p => p.AccountId == accountId))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        foreach (var meetId in meetIds)
            await TryExpireMeetAsync(meetId, cancellationToken);
    }

    private static bool CanAccessMeet(SnMeet meet, Guid accountId)
    {
        return meet.Visibility == MeetVisibility.Public
            || meet.HostId == accountId
            || meet.Participants.Any(p => p.AccountId == accountId);
    }

    private static Duration NormalizeTtl(Duration? ttl)
    {
        if (ttl is null) return DefaultTtl;
        if (ttl < MinimumTtl) return MinimumTtl;
        if (ttl > MaximumTtl) return MaximumTtl;
        return ttl.Value;
    }

    private async Task HydrateMeetAsync(SnMeet meet, CancellationToken cancellationToken)
    {
        meet.Host = await accounts.GetAccount(meet.HostId);
        foreach (var participant in meet.Participants)
            participant.Account = await accounts.GetAccount(participant.AccountId);
    }

    private async Task<SnCloudFileReferenceObject?> LoadImageAsync(string? imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId))
            return null;

        var request = new DyGetFileBatchRequest();
        request.Ids.Add(imageId);
        var response = await files.GetFileBatchAsync(request);
        var file = response.Files.FirstOrDefault();
        if (file is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Image was not found."));

        return SnCloudFileReferenceObject.FromProtoValue(file);
    }
}
