using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Realm;

[ApiController]
[Route("/realms")]
public class RealmController(AppDatabase db, RealmService rs, FileService fs, RelationshipService rels, ActionLogService als) : Controller
{
    [HttpGet("{slug}")]
    public async Task<ActionResult<Realm>> GetRealm(string slug)
    {
        var realm = await db.Realms
            .Where(e => e.Slug == slug)
            .Include(e => e.Picture)
            .Include(e => e.Background)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        return Ok(realm);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Realm>>> ListJoinedRealms()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var members = await db.RealmMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.JoinedAt != null)
            .Where(m => m.LeaveAt == null)
            .Include(e => e.Realm)
            .Select(m => m.Realm)
            .ToListAsync();

        return members.ToList();
    }

    [HttpGet("invites")]
    [Authorize]
    public async Task<ActionResult<List<RealmMember>>> ListInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var members = await db.RealmMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.JoinedAt == null)
            .Include(e => e.Realm)
            .ToListAsync();

        return members.ToList();
    }

    public class RealmMemberRequest
    {
        [Required] public Guid RelatedUserId { get; set; }
        [Required] public RealmMemberRole Role { get; set; }
    }

    [HttpPost("invites/{slug}")]
    [Authorize]
    public async Task<ActionResult<RealmMember>> InviteMember(string slug,
        [FromBody] RealmMemberRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var relatedUser = await db.Accounts.FindAsync(request.RelatedUserId);
        if (relatedUser is null) return BadRequest("Related user was not found");

        if (await rels.HasRelationshipWithStatus(currentUser.Id, relatedUser.Id, RelationshipStatus.Blocked))
            return StatusCode(403, "You cannot invite a user that blocked you.");

        var realm = await db.Realms
            .Where(p => p.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, userId, request.Role))
            return StatusCode(403, "You cannot invite member has higher permission than yours.");

        var hasExistingMember = await db.RealmMembers
            .Where(m => m.AccountId == request.RelatedUserId)
            .Where(m => m.RealmId == realm.Id)
            .Where(m => m.LeaveAt == null)
            .AnyAsync();
        if (hasExistingMember)
            return BadRequest("This user has been joined the realm or leave cannot be invited again.");

        var member = new RealmMember
        {
            AccountId = relatedUser.Id,
            RealmId = realm.Id,
            Role = request.Role,
        };

        db.RealmMembers.Add(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.RealmInvite,
            new Dictionary<string, object> { { "realm_id", realm.Id }, { "account_id", member.AccountId } }, Request
        );

        member.Account = relatedUser;
        member.Realm = realm;
        await rs.SendInviteNotify(member);

        return Ok(member);
    }

    [HttpPost("invites/{slug}/accept")]
    [Authorize]
    public async Task<ActionResult<Realm>> AcceptMemberInvite(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        db.Update(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.RealmJoin,
            new Dictionary<string, object> { { "realm_id", member.RealmId }, { "account_id", member.AccountId } },
            Request
        );

        return Ok(member);
    }

    [HttpPost("invites/{slug}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineMemberInvite(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.RealmLeave,
            new Dictionary<string, object> { { "realm_id", member.RealmId }, { "account_id", member.AccountId } },
            Request
        );

        return NoContent();
    }


    [HttpGet("{slug}/members")]
    public async Task<ActionResult<List<RealmMember>>> ListMembers(
        string slug,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!realm.IsPublic)
        {
            if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
            if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Normal))
                return StatusCode(403, "You must be a member to view this realm's members.");
        }

        var query = db.RealmMembers
            .Where(m => m.RealmId == realm.Id)
            .Where(m => m.LeaveAt == null);

        var total = await query.CountAsync();
        Response.Headers["X-Total"] = total.ToString();

        var members = await query
            .OrderBy(m => m.CreatedAt)
            .Skip(offset)
            .Take(take)
            .Include(m => m.Account)
            .Include(m => m.Account.Profile)
            .ToListAsync();

        return Ok(members);
    }

    [HttpGet("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult<RealmMember>> GetCurrentIdentity(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.Realm.Slug == slug)
            .Include(m => m.Account)
            .Include(m => m.Account.Profile)
            .FirstOrDefaultAsync();

        if (member is null) return NotFound();
        return Ok(member);
    }

    [HttpDelete("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult> LeaveRealm(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt != null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (member.Role == RealmMemberRole.Owner)
            return StatusCode(403, "Owner cannot leave their own realm.");

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.RealmLeave,
            new Dictionary<string, object> { { "realm_id", member.RealmId }, { "account_id", member.AccountId } },
            Request
        );

        return NoContent();
    }

    public class RealmRequest
    {
        [MaxLength(1024)] public string? Slug { get; set; }
        [MaxLength(1024)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        public string? PictureId { get; set; }
        public string? BackgroundId { get; set; }
        public bool? IsCommunity { get; set; }
        public bool? IsPublic { get; set; }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<Realm>> CreateRealm(RealmRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("You cannot create a realm without a name.");
        if (string.IsNullOrWhiteSpace(request.Slug)) return BadRequest("You cannot create a realm without a slug.");

        var slugExists = await db.Realms.AnyAsync(r => r.Slug == request.Slug);
        if (slugExists) return BadRequest("Realm with this slug already exists.");

        var realm = new Realm
        {
            Name = request.Name!,
            Slug = request.Slug!,
            Description = request.Description!,
            AccountId = currentUser.Id,
            IsCommunity = request.IsCommunity ?? false,
            IsPublic = request.IsPublic ?? false,
            Members = new List<RealmMember>
            {
                new()
                {
                    Role = RealmMemberRole.Owner,
                    AccountId = currentUser.Id,
                    JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        if (request.PictureId is not null)
        {
            realm.Picture = await db.Files.FindAsync(request.PictureId);
            if (realm.Picture is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
        }

        if (request.BackgroundId is not null)
        {
            realm.Background = await db.Files.FindAsync(request.BackgroundId);
            if (realm.Background is null) return BadRequest("Invalid background id, unable to find the file on cloud.");
        }

        db.Realms.Add(realm);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.RealmCreate,
            new Dictionary<string, object> { { "realm_id", realm.Id } }, Request
        );

        if (realm.Picture is not null) await fs.MarkUsageAsync(realm.Picture, 1);
        if (realm.Background is not null) await fs.MarkUsageAsync(realm.Background, 1);

        return Ok(realm);
    }

    [HttpPatch("{slug}")]
    [Authorize]
    public async Task<ActionResult<Realm>> Update(string slug, [FromBody] RealmRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .Include(r => r.Picture)
            .Include(r => r.Background)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var member = await db.RealmMembers
            .Where(m => m.AccountId == currentUser.Id && m.RealmId == realm.Id && m.JoinedAt != null)
            .FirstOrDefaultAsync();
        if (member is null || member.Role < RealmMemberRole.Moderator)
            return StatusCode(403, "You do not have permission to update this realm.");

        if (request.Slug is not null && request.Slug != realm.Slug)
        {
            var slugExists = await db.Realms.AnyAsync(r => r.Slug == request.Slug);
            if (slugExists) return BadRequest("Realm with this slug already exists.");
            realm.Slug = request.Slug;
        }

        if (request.Name is not null)
            realm.Name = request.Name;
        if (request.Description is not null)
            realm.Description = request.Description;
        if (request.IsCommunity is not null)
            realm.IsCommunity = request.IsCommunity.Value;
        if (request.IsPublic is not null)
            realm.IsPublic = request.IsPublic.Value;

        if (request.PictureId is not null)
        {
            var picture = await db.Files.FindAsync(request.PictureId);
            if (picture is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
            await fs.MarkUsageAsync(picture, 1);
            if (realm.Picture is not null) await fs.MarkUsageAsync(realm.Picture, -1);
            realm.Picture = picture;
        }

        if (request.BackgroundId is not null)
        {
            var background = await db.Files.FindAsync(request.BackgroundId);
            if (background is null) return BadRequest("Invalid background id, unable to find the file on cloud.");
            await fs.MarkUsageAsync(background, 1);
            if (realm.Background is not null) await fs.MarkUsageAsync(realm.Background, -1);
            realm.Background = background;
        }

        db.Realms.Update(realm);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.RealmUpdate,
            new Dictionary<string, object> { { "realm_id", realm.Id } }, Request
        );

        return Ok(realm);
    }

    [HttpPost("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult<RealmMember>> JoinRealm(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!realm.IsCommunity)
            return StatusCode(403, "Only community realms can be joined without invitation.");

        var existingMember = await db.RealmMembers
            .Where(m => m.AccountId == currentUser.Id && m.RealmId == realm.Id)
            .FirstOrDefaultAsync();
        if (existingMember is not null)
            return BadRequest("You are already a member of this realm.");

        var member = new RealmMember
        {
            AccountId = currentUser.Id,
            RealmId = realm.Id,
            Role = RealmMemberRole.Normal,
            JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        db.RealmMembers.Add(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.RealmJoin,
            new Dictionary<string, object> { { "realm_id", realm.Id }, { "account_id", currentUser.Id } },
            Request
        );

        return Ok(member);
    }

    [HttpDelete("{slug}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveMember(string slug, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var member = await db.RealmMembers
            .Where(m => m.AccountId == memberId && m.RealmId == realm.Id)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Moderator, member.Role))
            return StatusCode(403, "You do not have permission to remove members from this realm.");

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.ChatroomKick,
            new Dictionary<string, object> { { "realm_id", realm.Id }, { "account_id", memberId } },
            Request
        );

        return NoContent();
    }

    [HttpPatch("{slug}/members/{memberId:guid}/role")]
    [Authorize]
    public async Task<ActionResult<RealmMember>> UpdateMemberRole(string slug, Guid memberId,
        [FromBody] RealmMemberRole newRole)
    {
        if (newRole >= RealmMemberRole.Owner) return BadRequest("Unable to set realm member to owner or greater role.");
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var member = await db.RealmMembers
            .Where(m => m.AccountId == memberId && m.RealmId == realm.Id)
            .Include(m => m.Account)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Moderator, member.Role, newRole))
            return StatusCode(403, "You do not have permission to update member roles in this realm.");

        member.Role = newRole;
        db.RealmMembers.Update(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.RealmAdjustRole,
            new Dictionary<string, object>
                { { "realm_id", realm.Id }, { "account_id", memberId }, { "new_role", newRole } },
            Request
        );

        return Ok(member);
    }

    [HttpDelete("{slug}")]
    [Authorize]
    public async Task<ActionResult> Delete(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .Include(r => r.Picture)
            .Include(r => r.Background)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Owner))
            return StatusCode(403, "Only the owner can delete this realm.");

        db.Realms.Remove(realm);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.RealmDelete,
            new Dictionary<string, object> { { "realm_id", realm.Id } }, Request
        );

        if (realm.Picture is not null)
            await fs.MarkUsageAsync(realm.Picture, -1);
        if (realm.Background is not null)
            await fs.MarkUsageAsync(realm.Background, -1);

        return NoContent();
    }
}