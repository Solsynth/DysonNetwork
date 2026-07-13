using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

[ApiController]
[Route("/api/admin/publishers")]
[Authorize]
public class PublisherAdminController(
    AppDatabase db,
    PublisherService publisherService,
    RemoteActionLogService als
) : ControllerBase
{
    public class AdminUpdatePublisherRequest
    {
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(256)] public string? Nick { get; set; }
        [MaxLength(4096)] public string? Bio { get; set; }
        public bool? GatekeptFollows { get; set; }
        public bool? ModerateSubscription { get; set; }
    }

    public class SetPublisherShadowbanRequest
    {
        public PublisherShadowbanReason Reason { get; set; }
    }

    public class SetPublisherVerificationRequest
    {
        public VerificationMarkType Type { get; set; }
        [MaxLength(1024)] public string? Title { get; set; }
        [MaxLength(8192)] public string? Description { get; set; }
        [MaxLength(1024)] public string? VerifiedBy { get; set; }
    }

    public class AdminPublisherDetail
    {
        public SnPublisher Publisher { get; set; } = null!;
        public int MemberCount { get; set; }
        public int PostCount { get; set; }
        public int CollectionCount { get; set; }
        public int SubscriberCount { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.PublishersModerate)]
    public async Task<ActionResult<List<SnPublisher>>> ListPublishers(
        [FromQuery] string? query = null,
        [FromQuery] PublisherType? type = null,
        [FromQuery] PublisherShadowbanReason? shadowbanReason = null,
        [FromQuery] bool? shadowbanned = null,
        [FromQuery] bool? gatekept = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var publishersQuery = db.Publishers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var probe = query.Trim();
            publishersQuery = publishersQuery.Where(p =>
                EF.Functions.ILike(p.Name, $"%{probe}%") ||
                EF.Functions.ILike(p.Nick, $"%{probe}%") ||
                (p.Bio != null && EF.Functions.ILike(p.Bio, $"%{probe}%")));
        }

        if (type.HasValue)
            publishersQuery = publishersQuery.Where(p => p.Type == type.Value);
        if (accountId.HasValue)
            publishersQuery = publishersQuery.Where(p => p.AccountId == accountId.Value);
        if (shadowbanReason.HasValue)
            publishersQuery = publishersQuery.Where(p => p.ShadowbanReason == shadowbanReason.Value);
        if (shadowbanned == true)
            publishersQuery = publishersQuery.Where(p =>
                p.ShadowbanReason != null && p.ShadowbanReason != PublisherShadowbanReason.None);
        if (shadowbanned == false)
            publishersQuery = publishersQuery.Where(p =>
                p.ShadowbanReason == null || p.ShadowbanReason == PublisherShadowbanReason.None);
        if (gatekept.HasValue)
            publishersQuery = gatekept.Value
                ? publishersQuery.Where(p => p.GatekeptFollows == true)
                : publishersQuery.Where(p => p.GatekeptFollows != true);

        var total = await publishersQuery.CountAsync(HttpContext.RequestAborted);
        Response.Headers.Append("X-Total", total.ToString());

        var publishers = await publishersQuery
            .OrderByDescending(p => p.UpdatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync(HttpContext.RequestAborted);

        publishers = await publisherService.LoadIndividualPublisherAccounts(publishers);
        return Ok(publishers);
    }

    [HttpGet("{name}")]
    [AskPermission(PermissionKeys.PublishersModerate)]
    public async Task<ActionResult<AdminPublisherDetail>> GetPublisher(string name)
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name.ToLower() == name.ToLowerInvariant(), HttpContext.RequestAborted);
        if (publisher is null)
            return NotFound();

        var loaded = await publisherService.LoadIndividualPublisherAccounts([publisher]);
        publisher = loaded.First();
        publisher = (await publisherService.HydratePublisherRealm([publisher])).First();

        var detail = new AdminPublisherDetail
        {
            Publisher = publisher,
            MemberCount = await db.PublisherMembers
                .CountAsync(m => m.PublisherId == publisher.Id && m.JoinedAt != null, HttpContext.RequestAborted),
            PostCount = await db.Posts
                .CountAsync(p => p.PublisherId == publisher.Id, HttpContext.RequestAborted),
            CollectionCount = await db.PostCollections
                .CountAsync(c => c.PublisherId == publisher.Id, HttpContext.RequestAborted),
            SubscriberCount = await db.PublisherSubscriptions
                .CountAsync(s => s.PublisherId == publisher.Id && s.EndedAt == null, HttpContext.RequestAborted)
        };

        return Ok(detail);
    }

    [HttpPatch("{name}")]
    [AskPermission(PermissionKeys.PublishersUpdate)]
    public async Task<ActionResult<SnPublisher>> UpdatePublisher(
        string name,
        [FromBody] AdminUpdatePublisherRequest request
    )
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name.ToLower() == name.ToLowerInvariant(), HttpContext.RequestAborted);
        if (publisher is null)
            return NotFound();

        if (request.Name is not null)
        {
            var normalized = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return BadRequest("Name cannot be empty.");

            if (!string.Equals(normalized, publisher.Name, StringComparison.OrdinalIgnoreCase))
            {
                var exists = await db.Publishers.AnyAsync(
                    p => p.Name.ToLower() == normalized.ToLowerInvariant() && p.Id != publisher.Id,
                    HttpContext.RequestAborted
                );
                if (exists)
                    return BadRequest("A publisher with this name already exists.");
                publisher.Name = normalized;
            }
        }

        if (request.Nick is not null)
            publisher.Nick = request.Nick;
        if (request.Bio is not null)
            publisher.Bio = request.Bio;
        if (request.GatekeptFollows.HasValue)
            publisher.GatekeptFollows = request.GatekeptFollows.Value;
        if (request.ModerateSubscription.HasValue)
            publisher.ModerateSubscription = request.ModerateSubscription.Value;

        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogPublisherAction(ActionLogType.PublisherUpdate, publisher.Id, new Dictionary<string, object>
        {
            ["operation"] = "admin_update",
            ["publisher_name"] = publisher.Name
        });

        return Ok(publisher);
    }

    [HttpPost("{name}/shadowban")]
    [AskPermission(PermissionKeys.PublishersModerate)]
    public async Task<ActionResult<SnPublisher>> ShadowbanPublisher(
        string name,
        [FromBody] SetPublisherShadowbanRequest request
    )
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name.ToLower() == name.ToLowerInvariant(), HttpContext.RequestAborted);
        if (publisher is null)
            return NotFound();

        if (request.Reason == PublisherShadowbanReason.None)
            return BadRequest("Use DELETE to clear a shadowban.");

        publisher.ShadowbanReason = request.Reason;
        publisher.ShadowbannedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogPublisherAction(ActionLogType.PublishersModerate, publisher.Id, new Dictionary<string, object>
        {
            ["operation"] = "shadowban",
            ["reason"] = request.Reason.ToString()
        });

        return Ok(publisher);
    }

    [HttpDelete("{name}/shadowban")]
    [AskPermission(PermissionKeys.PublishersModerate)]
    public async Task<ActionResult<SnPublisher>> UnshadowbanPublisher(string name)
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name.ToLower() == name.ToLowerInvariant(), HttpContext.RequestAborted);
        if (publisher is null)
            return NotFound();

        publisher.ShadowbanReason = PublisherShadowbanReason.None;
        publisher.ShadowbannedAt = null;
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogPublisherAction(ActionLogType.PublishersModerate, publisher.Id, new Dictionary<string, object>
        {
            ["operation"] = "unshadowban"
        });

        return Ok(publisher);
    }

    [HttpPost("{name}/verification")]
    [AskPermission(PermissionKeys.PublishersModerate)]
    public async Task<ActionResult<SnPublisher>> SetVerification(
        string name,
        [FromBody] SetPublisherVerificationRequest request
    )
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name.ToLower() == name.ToLowerInvariant(), HttpContext.RequestAborted);
        if (publisher is null)
            return NotFound();

        publisher.Verification = new SnVerificationMark
        {
            Type = request.Type,
            Title = request.Title,
            Description = request.Description,
            VerifiedBy = request.VerifiedBy
        };
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogPublisherAction(ActionLogType.PublishersModerate, publisher.Id, new Dictionary<string, object>
        {
            ["operation"] = "set_verification",
            ["verification_type"] = request.Type.ToString()
        });

        return Ok(publisher);
    }

    [HttpDelete("{name}/verification")]
    [AskPermission(PermissionKeys.PublishersModerate)]
    public async Task<ActionResult<SnPublisher>> ClearVerification(string name)
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name.ToLower() == name.ToLowerInvariant(), HttpContext.RequestAborted);
        if (publisher is null)
            return NotFound();

        publisher.Verification = null;
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogPublisherAction(ActionLogType.PublishersModerate, publisher.Id, new Dictionary<string, object>
        {
            ["operation"] = "clear_verification"
        });

        return Ok(publisher);
    }

    [HttpDelete("{name}")]
    [AskPermission(PermissionKeys.PublishersDelete)]
    public async Task<IActionResult> DeletePublisher(string name)
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name.ToLower() == name.ToLowerInvariant(), HttpContext.RequestAborted);
        if (publisher is null)
            return NotFound();

        var publisherId = publisher.Id;
        var publisherName = publisher.Name;
        var publisherType = publisher.Type.ToString();

        db.Publishers.Remove(publisher);
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogPublisherAction(ActionLogType.PublisherDelete, publisherId, new Dictionary<string, object>
        {
            ["operation"] = "admin_delete",
            ["publisher_name"] = publisherName,
            ["publisher_type"] = publisherType
        });

        return NoContent();
    }

    private void LogPublisherAction(string action, Guid publisherId, Dictionary<string, object>? extraMeta = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return;

        var meta = new Dictionary<string, object> { ["publisher_id"] = publisherId.ToString() };
        if (extraMeta is not null)
        {
            foreach (var (key, value) in extraMeta)
                meta[key] = value;
        }

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            action,
            meta,
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );
    }
}
