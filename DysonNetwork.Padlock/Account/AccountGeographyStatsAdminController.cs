using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Padlock.Account;

[ApiController]
[Route("/api/admin/stats/users/geography")]
[Authorize]
public class AccountGeographyStatsAdminController(AppDatabase db) : ControllerBase
{
    public class AccountGeographyBucket
    {
        public string CountryCode { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? City { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public long UserCount { get; set; }
    }

    public class AccountGeographyStatsResponse
    {
        public Instant CalculatedAt { get; set; }
        public Instant Since { get; set; }
        public string Precision { get; set; } = string.Empty;
        public long AccountsWithLocation { get; set; }
        public List<AccountGeographyBucket> Buckets { get; set; } = [];
    }

    [HttpGet]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<AccountGeographyStatsResponse>> GetUserGeography(
        [FromQuery] Instant? since = null,
        [FromQuery] string precision = "country",
        CancellationToken cancellationToken = default
    )
    {
        var normalizedPrecision = (precision ?? "country").Trim().ToLowerInvariant();
        if (normalizedPrecision is not "country" and not "city")
            return BadRequest(new ApiError { Code = "PADLOCK_STATS_PRECISION_INVALID", Message = "Precision must be either country or city.", Status = 400 });

        var now = SystemClock.Instance.GetCurrentInstant();
        var startAt = since ?? now - Duration.FromDays(30);
        if (startAt > now)
            return BadRequest(new ApiError { Code = "PADLOCK_STATS_SINCE_FUTURE", Message = "Since cannot be in the future.", Status = 400 });

        var latestLocations = await db.AuthSessions
            .AsNoTracking()
            .Where(session => session.LastGrantedAt != null && session.LastGrantedAt >= startAt)
            .Where(session => session.Location != null)
            .GroupBy(session => session.AccountId)
            .Select(group => group
                .OrderByDescending(session => session.LastGrantedAt)
                .ThenByDescending(session => session.CreatedAt)
                .Select(session => session.Location!)
                .First())
            .ToListAsync(cancellationToken);

        var buckets = latestLocations
            .Where(location =>
                !string.IsNullOrWhiteSpace(location.CountryCode) &&
                location.Latitude.HasValue &&
                location.Longitude.HasValue &&
                (normalizedPrecision != "city" || !string.IsNullOrWhiteSpace(location.City)))
            .GroupBy(location => normalizedPrecision == "city"
                ? $"{location.CountryCode!.ToUpperInvariant()}:{location.City!.Trim()}"
                : location.CountryCode!.ToUpperInvariant())
            .Select(group => new
            {
                Location = group.First(),
                UserCount = (long)group.Count(),
                Latitude = group.Average(location => location.Latitude!.Value),
                Longitude = group.Average(location => location.Longitude!.Value)
            })
            .OrderByDescending(bucket => bucket.UserCount)
            .ThenBy(bucket => bucket.Location.CountryCode)
            .ThenBy(bucket => bucket.Location.City)
            .Select(bucket => new AccountGeographyBucket
            {
                CountryCode = bucket.Location.CountryCode!.ToUpperInvariant(),
                Country = bucket.Location.Country,
                City = normalizedPrecision == "city" ? bucket.Location.City : null,
                Latitude = Math.Round(bucket.Latitude, 1, MidpointRounding.AwayFromZero),
                Longitude = Math.Round(bucket.Longitude, 1, MidpointRounding.AwayFromZero),
                UserCount = bucket.UserCount
            })
            .ToList();

        return Ok(new AccountGeographyStatsResponse
        {
            CalculatedAt = now,
            Since = startAt,
            Precision = normalizedPrecision,
            AccountsWithLocation = latestLocations.LongCount(),
            Buckets = buckets
        });
    }
}
