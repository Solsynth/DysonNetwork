using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/friends")]
[ApiFeature("friends", Revision = 1)]
public class FriendsController(
    AppDatabase db,
    RelationshipService rels,
    AccountEventService events,
    DyAccountService.DyAccountServiceClient accountGrpc
) : ControllerBase
{
    public class FriendOverviewItem
    {
        public SnAccount Account { get; set; } = null!;
        public SnAccountStatus Status { get; set; } = null!;
        public List<SnPresenceActivity> Activities { get; set; } = [];
    }

    [HttpGet("overview")]
    [Authorize]
    public async Task<ActionResult<List<FriendOverviewItem>>> GetOverview([FromQuery] bool includeOffline = false)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var friendIds = await rels.ListAccountFriends(currentUser);

        var accounts = await accountGrpc.GetAccountBatchAsync(new DyGetAccountBatchRequest
        {
            Id = { friendIds.Select(x => x.ToString()) }
        }).ResponseAsync;

        var statuses = await events.GetStatuses(friendIds);
        var activities = await events.GetActiveActivitiesBatch(friendIds);

        var profilesTask = db.AccountProfiles
            .Where(p => friendIds.Contains(p.AccountId))
            .ToListAsync();
        await profilesTask;
        var profiles = profilesTask.Result
            .GroupBy(profile => profile.AccountId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(profile => profile.UpdatedAt)
                    .ThenByDescending(profile => profile.CreatedAt)
                    .First()
            );

        var accountsList = accounts.Accounts
            .Select(SnAccount.FromProtoValue)
            .Select(a =>
            {
                if (profiles.TryGetValue(a.Id, out var profile))
                    a.Profile = profile;
                return a;
            })
            .ToList();

        var onlineIds = statuses.Where(s => s.Value.IsOnline).Select(s => s.Key).ToHashSet();
        var activeIds = activities.Where(a => a.Value.Count > 0).Select(a => a.Key).ToHashSet();
        var visibleIds = onlineIds.Concat(activeIds).ToHashSet();

        if (visibleIds.Count == 0 || includeOffline)
        {
            var since = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromHours(24));
            var recentIds = await db.AccountStatuses
                .Where(s => friendIds.Contains(s.AccountId) && s.UpdatedAt >= since && s.DeletedAt == null)
                .Select(s => s.AccountId)
                .ToListAsync();

            if (includeOffline)
                visibleIds.UnionWith(recentIds);
            else
                visibleIds = recentIds.ToHashSet();
        }

        // ponytail: fallback to friends active in last 24h when nobody is online/has activities

        var result = (from account in accountsList
            let status = statuses.GetValueOrDefault(account.Id)
            where visibleIds.Contains(account.Id)
            let accountActivities = activities.GetValueOrDefault(account.Id, new List<SnPresenceActivity>())
            select new FriendOverviewItem
            {
                Account = account, Status = status ?? new SnAccountStatus { AccountId = account.Id },
                Activities = accountActivities
            }).ToList();

        return Ok(result);
    }
}
