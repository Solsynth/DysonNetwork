using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/admin/stats")]
[Authorize]
[ApiFeature("admin.stats", Revision = 1)]
public class AccountStatsAdminController(AppDatabase db,
    AccountService accountService
) : ControllerBase
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
        var stats = await accountService.GetAccountActivityStatsAsync(now);

        return Ok(new AccountStatsResponse
        {
            CalculatedAt = now,
            TotalProfiledAccounts = stats.TotalAccounts,
            ActiveUsersLastDay = stats.ActiveUsersLastDay,
            ActiveUsersLastWeek = stats.ActiveUsersLastWeek,
            ActiveUsersLastMonth = stats.ActiveUsersLastMonth,
            RegisteredUsersLastDay = stats.NewAccountsLastDay,
            RegisteredUsersLastWeek = stats.NewAccountsLastWeek,
            RegisteredUsersLastMonth = stats.NewAccountsLastMonth
        });
    }
}
