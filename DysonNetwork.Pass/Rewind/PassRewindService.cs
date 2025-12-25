using System.Linq;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Rewind;

/// <summary>
/// Although the pass uses the rewind service call internally, no need for grpc.
/// But we created a service that produce the grpc type for consistency.
/// </summary>
public class PassRewindService(AppDatabase db)
{
    public async Task<RewindEvent> CreateRewindEvent(Guid accountId, int year)
    {
        var startDate = Instant.FromDateTimeUtc(new DateTime(year - 1, 12, 26));
        var endDate = Instant.FromDateTimeUtc(new DateTime(year, 12, 26));

        var checkInDates = await db.AccountCheckInResults
            .Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(a => a.AccountId == accountId)
            .Select(a => a.CreatedAt.ToDateTimeUtc().Date)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync();

        var maxCheckInStrike = 0;
        if (checkInDates.Count != 0)
        {
            maxCheckInStrike = checkInDates
                .Select((d, i) => new { Date = d, Index = i })
                .GroupBy(x => x.Date.Subtract(new TimeSpan(x.Index, 0, 0, 0)))
                .Select(g => g.Count())
                .Max();
        }

        var data = new Dictionary<string, object?>
        {
            ["max_check_in_strike"] = maxCheckInStrike,
        };
        
        return new RewindEvent
        {
            ServiceId = "pass",
            AccountId = accountId.ToString(),
            Data = GrpcTypeHelper.ConvertObjectToByteString(data)
        };
    }
}
