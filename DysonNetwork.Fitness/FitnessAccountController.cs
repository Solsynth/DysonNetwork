using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Fitness;

[ApiController]
[Route("/api/fitness")]
[Authorize]
public class FitnessAccountController(
    AppDatabase db,
    ILogger<FitnessAccountController> logger
) : ControllerBase
{
    [HttpDelete("account")]
    public async Task<ActionResult> DeleteAllFitnessData()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var workoutCount = await db.Workouts.CountAsync(w => w.AccountId == accountId);
        var metricCount = await db.FitnessMetrics.CountAsync(m => m.AccountId == accountId);
        var goalCount = await db.FitnessGoals.CountAsync(g => g.AccountId == accountId);
        var totalCount = workoutCount + metricCount + goalCount;

        if (totalCount == 0)
        {
            return Ok(new { message = "No fitness data found" });
        }

        logger.LogInformation("Starting fitness data deletion for account {AccountId}. Total records: {Total}", 
            accountId, totalCount);

        var deletedWorkouts = await db.Workouts.Where(w => w.AccountId == accountId).ExecuteDeleteAsync();
        var deletedMetrics = await db.FitnessMetrics.Where(m => m.AccountId == accountId).ExecuteDeleteAsync();
        var deletedGoals = await db.FitnessGoals.Where(g => g.AccountId == accountId).ExecuteDeleteAsync();

        logger.LogInformation("Permanently deleted fitness data for account {AccountId}. Workouts: {Workouts}, Metrics: {Metrics}, Goals: {Goals}", 
            accountId, deletedWorkouts, deletedMetrics, deletedGoals);

        return Ok(new { 
            message = "All fitness data permanently deleted",
            deleted = new {
                workouts = deletedWorkouts,
                metrics = deletedMetrics,
                goals = deletedGoals,
            }
        });
    }

    [HttpGet("account")]
    public async Task<ActionResult<FitnessAccountDataSummary>> GetFitnessDataSummary()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var workoutCount = await db.Workouts.CountAsync(w => w.AccountId == accountId);
        var metricCount = await db.FitnessMetrics.CountAsync(m => m.AccountId == accountId);
        var goalCount = await db.FitnessGoals.CountAsync(g => g.AccountId == accountId);

        return Ok(new FitnessAccountDataSummary(
            workoutCount,
            metricCount,
            goalCount,
            workoutCount + metricCount + goalCount
        ));
    }
}

public record FitnessAccountDataSummary(
    int WorkoutsCount,
    int MetricsCount,
    int GoalsCount,
    int TotalCount
);
