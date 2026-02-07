using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.TimeZones;

namespace DysonNetwork.Pass.Rewind;

/// <summary>
/// Although the pass uses the rewind service call internally, no need for grpc.
/// But we created a service that produce the grpc type for consistency.
/// </summary>
public class PassRewindService(AppDatabase db)
{
    public async Task<RewindEvent> CreateRewindEvent(Guid accountId, int year)
    {
        var startDate = new LocalDate(year - 1, 12, 26).AtMidnight().InUtc().ToInstant();
        var endDate = new LocalDate(year, 12, 26).AtMidnight().InUtc().ToInstant();

        var timeZone = (await db.AccountProfiles
            .Where(p => p.AccountId == accountId)
            .FirstOrDefaultAsync())?.TimeZone;

        var zone = TimeZoneInfo.Utc;
        if (!string.IsNullOrEmpty(timeZone))
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            }
            catch (DateTimeZoneNotFoundException)
            {
                // use UTC
            }

        var newFriendsCount = await db.AccountRelationships
            .Where(r => r.CreatedAt >= startDate && r.CreatedAt < endDate)
            .Where(r => r.AccountId == accountId)
            .Where(r => r.Status == RelationshipStatus.Friends)
            .CountAsync();
        var newBlockedCount = await db.AccountRelationships
            .Where(r => r.CreatedAt >= startDate && r.CreatedAt < endDate)
            .Where(r => r.AccountId == accountId)
            .Where(r => r.Status == RelationshipStatus.Blocked)
            .CountAsync();

        var checkInDates = await db.AccountCheckInResults
            .Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(a => a.AccountId == accountId)
            .Select(a => a.CreatedAt.ToDateTimeUtc().Date)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync();

        var maxCheckInStreak = 0;
        if (checkInDates.Count != 0)
        {
            maxCheckInStreak = checkInDates
                .Select((d, i) => new { Date = d, Index = i })
                .GroupBy(x => x.Date.Subtract(new TimeSpan(x.Index, 0, 0, 0)))
                .Select(g => g.Count())
                .Max();
        }

        var checkInCompleteness = checkInDates.Count / 365.0;

        var actionDates = await db.ActionLogs
            .Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(a => a.AccountId == accountId)
            .Select(a => a.CreatedAt.ToDateTimeUtc().Date)
            .ToListAsync();

        var mostActiveDay = actionDates
            .GroupBy(d => d)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .FirstOrDefault();

        var mostActiveWeekday = actionDates
            .GroupBy(d => d.DayOfWeek)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .FirstOrDefault();

        var actionTimes = actionDates
            .Select(a => TimeZoneInfo.ConvertTimeFromUtc(a, zone).TimeOfDay)
            .ToList();

        TimeSpan? latestActiveTime = null;
        if (actionTimes.Count != 0)
        {
            var timesBefore6Am = actionTimes.Where(t => t < TimeSpan.FromHours(6)).ToList();
            latestActiveTime = timesBefore6Am.Count != 0 ? timesBefore6Am.Max() : actionTimes.Max();
        }

        var lotteriesQuery = db.Lotteries
            .Where(l => l.CreatedAt >= startDate && l.CreatedAt < endDate)
            .Where(l => l.AccountId == accountId)
            .AsQueryable();
        var lotteriesWins = await lotteriesQuery
            .Where(l => l.MatchedRegionOneNumbers != null && l.MatchedRegionOneNumbers.Count > 0)
            .CountAsync();
        var lotteriesLosses = await lotteriesQuery
            .Where(l => l.MatchedRegionOneNumbers == null || l.MatchedRegionOneNumbers.Count == 0)
            .CountAsync();
        var lotteriesWinRate = lotteriesWins / (double)(lotteriesWins + lotteriesLosses);

        var data = new Dictionary<string, object?>
        {
            ["max_check_in_streak"] = maxCheckInStreak,
            ["check_in_completeness"] = checkInCompleteness,
            ["most_active_day"] = mostActiveDay?.Date.ToString("yyyy-MM-dd"),
            ["most_active_weekday"] = mostActiveWeekday?.Day.ToString(),
            ["latest_active_time"] = latestActiveTime?.ToString(@"hh\:mm"),
            ["new_friends_count"] = newFriendsCount,
            ["new_blocked_count"] = newBlockedCount,
            ["lotteries_wins"] = lotteriesWins,
            ["lotteries_losses"] = lotteriesLosses,
            ["lotteries_win_rate"] = lotteriesWinRate,
        };

        return new RewindEvent
        {
            ServiceId = "pass",
            AccountId = accountId.ToString(),
            Data = InfraObjectCoder.ConvertObjectToByteString(data)
        };
    }
}
