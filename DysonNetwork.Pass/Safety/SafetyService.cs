using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Safety;

public class SafetyService(AppDatabase db, ILogger<SafetyService> logger)
{
    public async Task<SnAbuseReport> CreateReport(string resourceIdentifier, AbuseReportType type, string reason, Guid accountId)
    {
        // Check if a similar report already exists from this user
        var existingReport = await db.AbuseReports
            .Where(r => r.ResourceIdentifier == resourceIdentifier && 
                       r.AccountId == accountId && 
                       r.DeletedAt == null)
            .FirstOrDefaultAsync();

        if (existingReport != null)
        {
            throw new InvalidOperationException("You have already reported this content.");
        }

        var report = new SnAbuseReport
        {
            ResourceIdentifier = resourceIdentifier,
            Type = type,
            Reason = reason,
            AccountId = accountId
        };

        db.AbuseReports.Add(report);
        await db.SaveChangesAsync();

        logger.LogInformation("New abuse report created: {ReportId} for resource {ResourceId}", 
            report.Id, resourceIdentifier);

        return report;
    }
    
    public async Task<int> CountReports(bool includeResolved = false)
    {
        return await db.AbuseReports
            .Where(r => includeResolved || r.ResolvedAt == null)
            .CountAsync();
    }
    
    public async Task<int> CountUserReports(Guid accountId, bool includeResolved = false)
    {
        return await db.AbuseReports
            .Where(r => r.AccountId == accountId)
            .Where(r => includeResolved || r.ResolvedAt == null)
            .CountAsync();
    }

    public async Task<List<SnAbuseReport>> GetReports(int offset = 0, int take = 20, bool includeResolved = false)
    {
        return await db.AbuseReports
            .Where(r => includeResolved || r.ResolvedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(take)
            .Include(r => r.Account)
            .ToListAsync();
    }

    public async Task<List<SnAbuseReport>> GetUserReports(Guid accountId, int offset = 0, int take = 20, bool includeResolved = false)
    {
        return await db.AbuseReports
            .Where(r => r.AccountId == accountId)
            .Where(r => includeResolved || r.ResolvedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
    }

    public async Task<SnAbuseReport?> GetReportById(Guid id)
    {
        return await db.AbuseReports
            .Include(r => r.Account)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<SnAbuseReport> ResolveReport(Guid id, string resolution)
    {
        var report = await db.AbuseReports.FindAsync(id) ?? throw new KeyNotFoundException("Report not found");
        report.ResolvedAt = SystemClock.Instance.GetCurrentInstant();
        report.Resolution = resolution;

        await db.SaveChangesAsync();
        return report;
    }

    public async Task<int> GetPendingReportsCount()
    {
        return await db.AbuseReports
            .Where(r => r.ResolvedAt == null)
            .CountAsync();
    }
}
