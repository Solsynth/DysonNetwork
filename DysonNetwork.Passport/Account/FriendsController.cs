using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/friends")]
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var friendIds = await rels.ListAccountFriends(currentUser);

        // Fetch data in parallel using batch methods for better performance
        var accountsTask = accountGrpc.GetAccountBatchAsync(new DyGetAccountBatchRequest
        {
            Id = { friendIds.Select(x => x.ToString()) }
        }).ResponseAsync;

        var statusesTask = events.GetStatuses(friendIds);
        var activitiesTask = events.GetActiveActivitiesBatch(friendIds);

        // Wait for all data to be fetched
        await Task.WhenAll(accountsTask, statusesTask, activitiesTask);

        var profilesTask = db.AccountProfiles
            .Where(p => friendIds.Contains(p.AccountId))
            .ToDictionaryAsync(p => p.AccountId, p => p);
        await profilesTask;
        var profiles = profilesTask.Result;

        var accounts = accountsTask.Result.Accounts
            .Select(SnAccount.FromProtoValue)
            .Select(a =>
            {
                if (profiles.TryGetValue(a.Id, out var profile))
                    a.Profile = profile;
                return a;
            })
            .ToList();
        var statuses = statusesTask.Result;
        var activities = activitiesTask.Result;

        var result = (from account in accounts
            let status = statuses.GetValueOrDefault(account.Id)
            where includeOffline || status is { IsOnline: true }
            let accountActivities = activities.GetValueOrDefault(account.Id, new List<SnPresenceActivity>())
            select new FriendOverviewItem
            {
                Account = account, Status = status ?? new SnAccountStatus { AccountId = account.Id },
                Activities = accountActivities
            }).ToList();

        return Ok(result);
    }
}
