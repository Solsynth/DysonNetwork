using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NodaTime;
using System.Text.Json;

namespace DysonNetwork.Passport.Meet;

public class MeetService(
    AppDatabase db,
    AccountService accounts,
    RelationshipService relationships,
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
        var candidates = await db.Meets
            .Include(m => m.Participants)
            .Where(m => m.HostId == hostId)
            .Where(m => m.Status == MeetStatus.Active)
            .Where(m => m.ExpiresAt > now)
            .Where(m => m.CreatedAt >= now - DuplicateWindow)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        var existing = candidates.FirstOrDefault(m =>
            m.Visibility == visibility
            && m.Notes == notes
            && m.LocationName == locationName
            && m.LocationAddress == locationAddress
            && SameGeometry(m.Location, location)
            && SameFileRef(m.Image, imageId)
            && SameMetadata(m.Metadata, metadata));

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
            var allMeets = await query.ToListAsync(cancellationToken);
            var accessibleMeets = new List<SnMeet>();
            foreach (var meet in allMeets)
            {
                if (await CanAccessMeetAsync(meet, accountId))
                    accessibleMeets.Add(meet);
            }
            
            if (status.HasValue)
                accessibleMeets = accessibleMeets.Where(m => m.Status == status.Value).ToList();
            
            var result = accessibleMeets
                .OrderByDescending(m => m.CreatedAt)
                .Skip(offset)
                .Take(take)
                .ToList();
            
            foreach (var meet in result)
                await HydrateMeetAsync(meet, cancellationToken);
            
            return result;
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

    public async Task<List<SnMeet>> ListNearbyMeetsAsync(
        Guid accountId,
        Geometry location,
        double distanceMeters = 1000,
        MeetStatus? status = MeetStatus.Active,
        int offset = 0,
        int take = 20,
        CancellationToken cancellationToken = default
    )
    {
        await ExpireMeetsAsync(accountId, cancellationToken);
        var friendIds = await relationships.ListAccountFriends(accountId);
        var limitedTake = Math.Clamp(take, 1, 100);

        // Query meets within distance that are Public or user's own
        var nearbyMeets = await db.Meets
            .Include(m => m.Participants)
            .Where(m => m.Location != null)
            .Where(m => m.Location.IsWithinDistance(location, distanceMeters))
            .Where(m => m.Visibility == MeetVisibility.Public
                || m.HostId == accountId
                || m.Participants.Any(p => p.AccountId == accountId))
            .ToListAsync(cancellationToken);

        // Also get Unlisted meets where user is friend of host or participant
        var unlistedMeets = await db.Meets
            .Include(m => m.Participants)
            .Where(m => m.Location != null)
            .Where(m => m.Location.IsWithinDistance(location, distanceMeters))
            .Where(m => m.Visibility == MeetVisibility.Unlisted)
            .Where(m => friendIds.Contains(m.HostId) 
                || m.Participants.Any(p => friendIds.Contains(p.AccountId)))
            .ToListAsync(cancellationToken);

        var allMeets = nearbyMeets.Concat(unlistedMeets)
            .DistinctBy(m => m.Id)
            .Where(m => !status.HasValue || m.Status == status.Value)
            .OrderBy(m => m.Location!.Distance(location))
            .Skip(offset)
            .Take(limitedTake)
            .ToList();

        foreach (var meet in allMeets)
            await HydrateMeetAsync(meet, cancellationToken);

        return allMeets;
    }

    public async Task<SnMeet?> GetMeetAsync(Guid meetId, Guid accountId, Geometry? userLocation = null, CancellationToken cancellationToken = default)
    {
        await TryExpireMeetAsync(meetId, cancellationToken);

        var meet = await db.Meets
            .Include(m => m.Participants)
            .FirstOrDefaultAsync(m => m.Id == meetId, cancellationToken);
        if (meet is null) return null;

        bool canAccess;
        if (userLocation != null)
        {
            canAccess = await CanAccessMeetAsync(meet, accountId, userLocation);
        }
        else
        {
            canAccess = await CanAccessMeetAsync(meet, accountId);
        }

        if (!canAccess) return null;

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

        if (!await CanAccessMeetAsync(meet, accountId))
            throw new UnauthorizedAccessException("You do not have access to this meet.");
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

    private const double PublicMeetMaxDistanceMeters = 5000;

    private async Task<bool> CanAccessMeetAsync(SnMeet meet, Guid accountId)
    {
        if (meet.HostId == accountId) return true;
        if (meet.Participants.Any(p => p.AccountId == accountId)) return true;

        var participantIds = meet.Participants.Select(p => p.AccountId).Append(meet.HostId).Distinct();
        foreach (var participantId in participantIds)
        {
            if (await relationships.HasRelationshipWithStatus(participantId, accountId))
                return true;
        }

        return false;
    }

    private async Task<bool> CanAccessMeetAsync(SnMeet meet, Guid accountId, Geometry userLocation)
    {
        if (meet.HostId == accountId) return true;
        if (meet.Participants.Any(p => p.AccountId == accountId)) return true;

        var participantIds = meet.Participants.Select(p => p.AccountId).Append(meet.HostId).Distinct();
        foreach (var participantId in participantIds)
        {
            if (await relationships.HasRelationshipWithStatus(participantId, accountId))
                return true;
        }

        if (meet.Visibility == MeetVisibility.Public)
        {
            if (meet.Location == null) return false;
            var distance = CalculateDistance(meet.Location, userLocation);
            return distance <= PublicMeetMaxDistanceMeters;
        }

        return false;
    }

    private static double CalculateDistance(Geometry location1, Geometry location2)
    {
        return location1.Distance(location2) * 111320;
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

    private static bool SameFileRef(SnCloudFileReferenceObject? image, string? imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId))
            return image is null;
        return image?.Id == imageId;
    }

    private static bool SameGeometry(Geometry? left, Geometry? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.SRID == right.SRID && string.Equals(left.AsText(), right.AsText(), StringComparison.Ordinal);
    }

    private static bool SameMetadata(Dictionary<string, object> existing, Dictionary<string, object>? incoming)
    {
        incoming ??= [];
        return CanonicalizeJson(existing) == CanonicalizeJson(incoming);
    }

    private static string CanonicalizeJson(object? value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        return CanonicalizeElement(element);
    }

    private static string CanonicalizeElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => "{"
                + string.Join(",",
                    element.EnumerateObject()
                        .OrderBy(p => p.Name, StringComparer.Ordinal)
                        .Select(p => JsonSerializer.Serialize(p.Name) + ":" + CanonicalizeElement(p.Value)))
                + "}",
            JsonValueKind.Array => "["
                + string.Join(",", element.EnumerateArray().Select(CanonicalizeElement))
                + "]",
            _ => element.GetRawText()
        };
    }
}
