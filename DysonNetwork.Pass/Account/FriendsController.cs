using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Account;

[ApiController]
[Route("/api/friends")]
public class FriendsController(AppDatabase db, RelationshipService rels, AccountEventService events) : ControllerBase
{
    public class FriendOverviewItem
    {
        public SnAccount Account { get; set; } = null!;
        public SnAccountStatus Status { get; set; } = null!;
        public List<SnPresenceActivity> Activities { get; set; } = [];
    }

    [HttpGet("overview")]
    [Authorize]
    public async Task<ActionResult<List<FriendOverviewItem>>> GetOverview()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var friendIds = await rels.ListAccountFriends(currentUser);
        
        // Fetch data in parallel using batch methods for better performance
        var accountsTask = db.Accounts
            .Where(a => friendIds.Contains(a.Id))
            .Include(a => a.Profile)
            .ToListAsync();
            
        var statusesTask = events.GetStatuses(friendIds);
        var activitiesTask = events.GetActiveActivitiesBatch(friendIds);
        
        // Wait for all data to be fetched
        await Task.WhenAll(accountsTask, statusesTask, activitiesTask);
        
        var accounts = accountsTask.Result;
        var statuses = statusesTask.Result;
        var activities = activitiesTask.Result;
        
        var result = new List<FriendOverviewItem>();
        
        foreach (var account in accounts)
        {
            var status = statuses.GetValueOrDefault(account.Id);
            var accountActivities = activities.GetValueOrDefault(account.Id, []);
            
            result.Add(new FriendOverviewItem
            {
                Account = account,
                Status = status ?? new SnAccountStatus { AccountId = account.Id },
                Activities = accountActivities
            });
        }
        
        return Ok(result);
    }
}
