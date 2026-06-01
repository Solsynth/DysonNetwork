using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Account;

[Authorize]
[ApiController]
[Route("/api/notable-days")]
public class NotableDaysController(
    AppDatabase db,
    NotableDaysService notableDaysService
) : ControllerBase
{
    public class NotableDayRequest
    {
        [Required, MaxLength(256)]
        public string Name { get; set; } = null!;

        [MaxLength(4096)]
        public string? Description { get; set; }

        [MaxLength(256)]
        public string? LocalName { get; set; }

        [MaxLength(256)]
        public string? LocalizableKey { get; set; }

        [Required]
        public Instant StartDate { get; set; }

        [Required]
        public Instant EndDate { get; set; }

        public bool IsAllDay { get; set; } = true;

        [MaxLength(8)]
        public string Region { get; set; } = "CN";

        public List<NotableDayTag> Tags { get; set; } = [];

        public Dictionary<string, object>? Meta { get; set; }

        public bool IsRecurring { get; set; }

        [MaxLength(16)]
        public string? RecurrencePattern { get; set; }

        public bool IsPeriod { get; set; }

        public List<string>? HolidayDays { get; set; }

        public int? DisplayOrder { get; set; }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<SnNotableDay>>> ListNotableDays(
        [FromQuery] int? year,
        [FromQuery] string region = "CN",
        [FromQuery] string? tag = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50)
    {
        year ??= SystemClock.Instance.GetCurrentInstant().InUtc().Year;

        var startOfYear = Instant.FromDateTimeUtc(new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var endOfYear = Instant.FromDateTimeUtc(new DateTime(year.Value + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var query = db.NotableDays
            .AsNoTracking()
            .Where(n => n.DeletedAt == null
                && n.Region == region
                && n.StartDate < endOfYear
                && n.EndDate >= startOfYear);

        if (!string.IsNullOrWhiteSpace(tag) && Enum.TryParse<NotableDayTag>(tag, true, out var tagEnum))
        {
            query = query.Where(n => n.Tags.Contains(tagEnum));
        }

        var totalCount = await query.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        var days = await query
            .OrderBy(n => n.DisplayOrder ?? 999)
            .ThenBy(n => n.StartDate)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(days);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<SnNotableDay>> GetNotableDay(Guid id)
    {
        var day = await db.NotableDays
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id && n.DeletedAt == null);

        if (day is null)
            return NotFound();

        return Ok(day);
    }

    [HttpPost]
    [AskPermission("notable-days.create")]
    public async Task<ActionResult<SnNotableDay>> CreateNotableDay([FromBody] NotableDayRequest request)
    {
        if (request.EndDate <= request.StartDate && !request.IsPeriod)
            return BadRequest("End date must be after start date");

        var notableDay = new SnNotableDay
        {
            Name = request.Name,
            Description = request.Description,
            LocalName = request.LocalName,
            LocalizableKey = request.LocalizableKey,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsAllDay = request.IsAllDay,
            Region = request.Region,
            Tags = request.Tags,
            Meta = request.Meta,
            IsRecurring = request.IsRecurring,
            RecurrencePattern = request.RecurrencePattern,
            IsPeriod = request.IsPeriod,
            HolidayDays = request.HolidayDays,
            DisplayOrder = request.DisplayOrder,
        };

        db.NotableDays.Add(notableDay);
        await db.SaveChangesAsync();
        await notableDaysService.PurgeCache(notableDay.Region);

        return Ok(notableDay);
    }

    [HttpPut("{id:guid}")]
    [AskPermission("notable-days.update")]
    public async Task<ActionResult<SnNotableDay>> UpdateNotableDay(Guid id, [FromBody] NotableDayRequest request)
    {
        var day = await db.NotableDays
            .FirstOrDefaultAsync(n => n.Id == id && n.DeletedAt == null);

        if (day is null)
            return NotFound();

        day.Name = request.Name;
        day.Description = request.Description;
        day.LocalName = request.LocalName;
        day.LocalizableKey = request.LocalizableKey;
        day.StartDate = request.StartDate;
        day.EndDate = request.EndDate;
        day.IsAllDay = request.IsAllDay;
        day.Region = request.Region;
        day.Tags = request.Tags;
        day.Meta = request.Meta;
        day.IsRecurring = request.IsRecurring;
        day.RecurrencePattern = request.RecurrencePattern;
        day.IsPeriod = request.IsPeriod;
        day.HolidayDays = request.HolidayDays;
        day.DisplayOrder = request.DisplayOrder;

        await db.SaveChangesAsync();
        await notableDaysService.PurgeCache(day.Region);

        return Ok(day);
    }

    [HttpDelete("{id:guid}")]
    [AskPermission("notable-days.delete")]
    public async Task<ActionResult> DeleteNotableDay(Guid id)
    {
        var day = await db.NotableDays
            .FirstOrDefaultAsync(n => n.Id == id && n.DeletedAt == null);

        if (day is null)
            return NotFound();

        day.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
        await notableDaysService.PurgeCache(day.Region);

        return NoContent();
    }
}
