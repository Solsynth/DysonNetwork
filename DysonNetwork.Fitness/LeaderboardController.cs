using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Fitness.Goals;
using NodaTime;

namespace DysonNetwork.Fitness;

[ApiController]
[Route("/api/leaderboard")]
[Authorize]
public class LeaderboardController(
    AppDatabase db,
    GoalService goalService,
    DyProfileService.DyProfileServiceClient profileClient,
    ILogger<LeaderboardController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LeaderboardResponse>> GetLeaderboard(
        [FromQuery] LeaderboardType type = LeaderboardType.Calories,
        [FromQuery] LeaderboardPeriod period = LeaderboardPeriod.Weekly,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var userId = Guid.Parse(currentUser.Id);

        var (startDate, endDate) = GetDateRange(period);

        var friendIds = await GetFriendIdsAsync(userId);
        var allIds = friendIds.Append(userId).ToList();

        var entries = type switch
        {
            LeaderboardType.Calories => await GetCaloriesLeaderboardAsync(allIds, userId, startDate, endDate, skip, take),
            LeaderboardType.Workouts => await GetWorkoutsLeaderboardAsync(allIds, userId, startDate, endDate, skip, take),
            LeaderboardType.Goals => await GetGoalsLeaderboardAsync(allIds, userId, startDate, endDate, skip, take),
            _ => new List<LeaderboardEntry>()
        };

        entries = await EnrichWithAccountDataAsync(entries);

        var totalCount = entries.Count;
        var userEntry = entries.FirstOrDefault(e => e.AccountId == userId);

        return Ok(new LeaderboardResponse(entries, userEntry, totalCount));
    }

    private async Task<List<Guid>> GetFriendIdsAsync(Guid userId)
    {
        try
        {
            var response = await profileClient.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = userId.ToString() }
            );
            return response.AccountsId.Select(Guid.Parse).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get friends for user {UserId}", userId);
            return new List<Guid>();
        }
    }

    private static (Instant Start, Instant End) GetDateRange(LeaderboardPeriod period)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var nowUtc = now.ToDateTimeUtc();

        return period switch
        {
            LeaderboardPeriod.Daily => (Instant.FromDateTimeUtc(nowUtc.Date), now),
            LeaderboardPeriod.Weekly => (Instant.FromDateTimeUtc(GetStartOfWeek(nowUtc)), now),
            LeaderboardPeriod.Monthly => (Instant.FromDateTimeUtc(new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc)), now),
            LeaderboardPeriod.AllTime => (Instant.FromDateTimeUtc(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)), now),
            _ => (Instant.FromDateTimeUtc(GetStartOfWeek(nowUtc)), now)
        };
    }

    private static DateTime GetStartOfWeek(DateTime dt)
    {
        var diff = (7 + (dt.DayOfWeek - DayOfWeek.Monday)) % 7;
        return dt.Date.AddDays(-diff);
    }

    private async Task<List<LeaderboardEntry>> GetCaloriesLeaderboardAsync(
        List<Guid> accountIds, Guid userId, Instant startDate, Instant endDate, int skip, int take)
    {
        var data = await db.Workouts
            .Where(w => accountIds.Contains(w.AccountId) && w.DeletedAt == null)
            .Where(w => w.StartTime >= startDate && w.StartTime <= endDate)
            .GroupBy(w => w.AccountId)
            .Select(g => new { AccountId = g.Key, Total = g.Sum(w => w.CaloriesBurned ?? 0) })
            .OrderByDescending(x => x.Total)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var rank = skip + 1;
        return data.Select(x => new LeaderboardEntry
        {
            Rank = rank++,
            AccountId = x.AccountId,
            Value = x.Total
        }).ToList();
    }

    private async Task<List<LeaderboardEntry>> GetWorkoutsLeaderboardAsync(
        List<Guid> accountIds, Guid userId, Instant startDate, Instant endDate, int skip, int take)
    {
        var data = await db.Workouts
            .Where(w => accountIds.Contains(w.AccountId) && w.DeletedAt == null)
            .Where(w => w.StartTime >= startDate && w.StartTime <= endDate)
            .GroupBy(w => w.AccountId)
            .Select(g => new { AccountId = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var rank = skip + 1;
        return data.Select(x => new LeaderboardEntry
        {
            Rank = rank++,
            AccountId = x.AccountId,
            Value = x.Total
        }).ToList();
    }

    private async Task<List<LeaderboardEntry>> GetGoalsLeaderboardAsync(
        List<Guid> accountIds, Guid userId, Instant startDate, Instant endDate, int skip, int take)
    {
        var data = await db.FitnessGoals
            .Where(g => accountIds.Contains(g.AccountId) && g.DeletedAt == null)
            .Where(g => g.Status == FitnessGoalStatus.Completed)
            .Where(g => g.UpdatedAt >= startDate && g.UpdatedAt <= endDate)
            .GroupBy(g => g.AccountId)
            .Select(g => new { AccountId = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var rank = skip + 1;
        return data.Select(x => new LeaderboardEntry
        {
            Rank = rank++,
            AccountId = x.AccountId,
            Value = x.Total
        }).ToList();
    }

    private async Task<List<LeaderboardEntry>> EnrichWithAccountDataAsync(List<LeaderboardEntry> entries)
    {
        if (entries.Count == 0)
            return entries;

        var accountIds = entries.Select(e => e.AccountId.ToString()).ToList();
        var response = await profileClient.GetAccountBatchAsync(new DyGetAccountBatchRequest { Id = { accountIds } });

        var accountsById = response.Accounts
            .Where(a => a != null)
            .Select(SnAccount.FromProtoValue)
            .ToDictionary(a => a.Id);

        return entries.Select(e => 
        {
            accountsById.TryGetValue(e.AccountId, out var account);
            return e with { Account = account };
        }).ToList();
    }

    public record LeaderboardEntry
    {
        public int Rank { get; init; }
        public Guid AccountId { get; init; }
        public int Value { get; init; }
        public SnAccount? Account { get; init; }
    }

    public record LeaderboardResponse(
        List<LeaderboardEntry> Entries,
        LeaderboardEntry? UserEntry,
        int TotalCount
    );
}
