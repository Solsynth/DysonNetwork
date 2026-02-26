using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Drive.Billing;

[ApiController]
[Route("/api/billing/quota")]
public class QuotaController(AppDatabase db, QuotaService quota) : ControllerBase
{
    public class QuotaDetails
    {
        public long BasedQuota { get; set; }
        public long ExtraQuota { get; set; }
        public long TotalQuota { get; set; }
    }
    
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<QuotaDetails>> GetQuota()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        
        var (based, extra) = await quota.GetQuotaVerbose(accountId);
        return Ok(new QuotaDetails
        {
            BasedQuota = based,
            ExtraQuota = extra,
            TotalQuota = based + extra
        });
    }
    
    [HttpGet("records")]
    [Authorize]
    public async Task<ActionResult<List<QuotaRecord>>> GetQuotaRecords(
        [FromQuery] bool expired = false,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var now = SystemClock.Instance.GetCurrentInstant();
        var query = db.QuotaRecords
            .Where(r => r.AccountId == accountId)
            .AsQueryable();
        if (!expired)
            query = query
                .Where(r => !r.ExpiredAt.HasValue || r.ExpiredAt > now);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var records = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(records);
    }
}