using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/admin/stats")]
[Authorize]
public class AccountStatsAdminController(AppDatabase db) : ControllerBase
{
    public class AccountStatsResponse
    {
        public Instant CalculatedAt { get; set; }
        public long TotalProfiledAccounts { get; set; }
        public long ActiveUsersLastDay { get; set; }
        public long ActiveUsersLastWeek { get; set; }
        public long ActiveUsersLastMonth { get; set; }
        public long RegisteredUsersLastDay { get; set; }
        public long RegisteredUsersLastWeek { get; set; }
        public long RegisteredUsersLastMonth { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<AccountStatsResponse>> GetStats(CancellationToken cancellationToken)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var oneDayAgo = now - Duration.FromDays(1);
        var sevenDaysAgo = now - Duration.FromDays(7);
        var thirtyDaysAgo = now - Duration.FromDays(30);
        var profiles = db.AccountProfiles.AsNoTracking();

        return Ok(new AccountStatsResponse
        {
            CalculatedAt = now,
            TotalProfiledAccounts = await profiles.LongCountAsync(cancellationToken),
            ActiveUsersLastDay = await profiles.LongCountAsync(p => p.LastSeenAt >= oneDayAgo, cancellationToken),
            ActiveUsersLastWeek = await profiles.LongCountAsync(p => p.LastSeenAt >= sevenDaysAgo, cancellationToken),
            ActiveUsersLastMonth = await profiles.LongCountAsync(p => p.LastSeenAt >= thirtyDaysAgo, cancellationToken),
            RegisteredUsersLastDay = await profiles.LongCountAsync(p => p.CreatedAt >= oneDayAgo, cancellationToken),
            RegisteredUsersLastWeek = await profiles.LongCountAsync(p => p.CreatedAt >= sevenDaysAgo, cancellationToken),
            RegisteredUsersLastMonth = await profiles.LongCountAsync(p => p.CreatedAt >= thirtyDaysAgo, cancellationToken)
        });
    }
}
