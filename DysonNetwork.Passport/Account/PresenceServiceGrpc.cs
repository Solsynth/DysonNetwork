using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Passport.Account;

public class PresenceServiceGrpc(
    AppDatabase db
) : DyPresenceService.DyPresenceServiceBase
{
    private const int DefaultMaxPerType = 3;
    private const int DefaultTake = 20;
    private static readonly Duration LookbackWindow = Duration.FromHours(24);

    public override async Task<DyListFriendsActivitiesResponse> ListFriendsActivities(
        DyListFriendsActivitiesRequest request,
        ServerCallContext context
    )
    {
        var accountIds = ParseAccountIds(request.AccountIds);
        if (accountIds.Count == 0)
            return new DyListFriendsActivitiesResponse();

        var maxPerType = request.MaxPerType > 0 ? request.MaxPerType : DefaultMaxPerType;
        var take = request.Take > 0 ? request.Take : DefaultTake;
        var now = SystemClock.Instance.GetCurrentInstant();
        var lookbackStart = now.Minus(LookbackWindow);

        var query = db.PresenceActivities
            .Where(e => accountIds.Contains(e.AccountId))
            .Where(e => e.CreatedAt >= lookbackStart)
            .Where(e => e.DeletedAt == null)
            .AsNoTracking();

        if (request.Cursor != null)
        {
            var cursorInstant = request.Cursor.ToInstant();
            query = query.Where(e => e.UpdatedAt < cursorInstant);
        }

        var allActivities = await query
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync(context.CancellationToken);

        var limitedActivities = allActivities
            .GroupBy(e => e.Type)
            .SelectMany(g => g.Take(maxPerType))
            .OrderByDescending(e => e.UpdatedAt)
            .Take(take)
            .ToList();

        var response = new DyListFriendsActivitiesResponse();
        foreach (var activity in limitedActivities)
            response.Activities.Add(activity.ToProtoValue());

        if (limitedActivities.Count == take)
            response.NextCursor = limitedActivities.Last().UpdatedAt.ToTimestamp();

        return response;
    }

    private static List<Guid> ParseAccountIds(IEnumerable<string> ids)
    {
        var result = new List<Guid>();
        foreach (var id in ids)
        {
            if (Guid.TryParse(id, out var guid))
                result.Add(guid);
        }
        return result;
    }
}
