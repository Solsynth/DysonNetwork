using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass.Account;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using AccountService = DysonNetwork.Pass.Account.AccountService;
using ActionLogService = DysonNetwork.Pass.Account.ActionLogService;

namespace DysonNetwork.Pass.Realm;

[ApiController]
[Route("/api/realms")]
public class RealmController(
    AppDatabase db,
    RealmService rs,
    DyFileService.DyFileServiceClient files,
    ActionLogService als,
    RelationshipService rels,
    AccountEventService accountEvents
) : Controller
{
    [HttpGet("{slug}")]
    public async Task<ActionResult<SnRealm>> GetRealm(string slug)
    {
        var realm = await db.Realms
            .Where(e => e.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        return Ok(realm);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnRealm>>> ListJoinedRealms()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var members = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .Include(e => e.Realm)
            .Select(m => m.Realm)
            .ToListAsync();

        return members.ToList();
    }

    [HttpGet("invites")]
    [Authorize]
    public async Task<ActionResult<List<SnRealmMember>>> ListInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var members = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt == null && m.LeaveAt == null)
            .Include(e => e.Realm)
            .ToListAsync();

        return await rs.LoadMemberAccounts(members);
    }

    public class RealmMemberRequest
    {
        [Required] public Guid RelatedUserId { get; set; }
        [Required] public int Role { get; set; }
    }

    [HttpPost("invites/{slug}")]
    [Authorize]
    public async Task<ActionResult<SnRealmMember>> InviteMember(string slug,
        [FromBody] RealmMemberRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var relatedUser = await db.Accounts.Where(a => a.Id == request.RelatedUserId).FirstOrDefaultAsync();
        if (relatedUser == null) return BadRequest("Related user was not found");

        var hasBlocked = await rels.HasRelationshipWithStatus(
            currentUser.Id,
            request.RelatedUserId,
            RelationshipStatus.Blocked
        );
        if (hasBlocked)
            return StatusCode(403, "You cannot invite a user that blocked you.");

        var realm = await db.Realms
            .Where(p => p.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, accountId, request.Role))
            return StatusCode(403, "You cannot invite member has higher permission than yours.");

        var existingMember = await db.RealmMembers
            .Where(m => m.AccountId == relatedUser.Id)
            .Where(m => m.RealmId == realm.Id)
            .FirstOrDefaultAsync();
        if (existingMember != null)
        {
            if (existingMember.LeaveAt == null)
                return BadRequest("This user already in the realm cannot be invited again.");

            existingMember.LeaveAt = null;
            existingMember.JoinedAt = null;
            db.RealmMembers.Update(existingMember);
            await db.SaveChangesAsync();
            await rs.SendInviteNotify(existingMember);

            als.CreateActionLogFromRequest(
                "realms.members.invite",
                new Dictionary<string, object>()
                {
                    { "realm_id", Value.ForString(realm.Id.ToString()) },
                    { "account_id", Value.ForString(existingMember.AccountId.ToString()) },
                    { "role", Value.ForNumber(request.Role) }
                },
                Request
            );

            return Ok(existingMember);
        }

        var member = new SnRealmMember
        {
            AccountId = relatedUser.Id,
            RealmId = realm.Id,
            Role = request.Role,
        };

        db.RealmMembers.Add(member);
        await db.SaveChangesAsync();
        
        als.CreateActionLogFromRequest(
            "realms.members.invite",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "account_id", Value.ForString(member.AccountId.ToString()) },
                { "role", Value.ForNumber(request.Role) }
            },
            Request
        );

        member.AccountId = relatedUser.Id;
        member.Realm = realm;
        await rs.SendInviteNotify(member);

        return Ok(member);
    }

    [HttpPost("invites/{slug}/accept")]
    [Authorize]
    public async Task<ActionResult<SnRealm>> AcceptMemberInvite(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        db.Update(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.join",
            new Dictionary<string, object>()
            {
                { "realm_id", member.RealmId.ToString() },
                { "account_id", member.AccountId.ToString() }
            },
            Request
        );

        return Ok(member);
    }

    [HttpPost("invites/{slug}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineMemberInvite(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.decline_invite",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(member.RealmId.ToString()) },
                { "account_id", Value.ForString(member.AccountId.ToString()) },
                { "decliner_id", Value.ForString(currentUser.Id.ToString()) }
            },
            Request
        );

        return NoContent();
    }


    [HttpGet("{slug}/members")]
    public async Task<ActionResult<List<SnRealmMember>>> ListMembers(
        string slug,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] bool withStatus = false
    )
    {
        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!realm.IsPublic)
        {
            if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
            if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.DyNormal))
                return StatusCode(403, "You must be a member to view this realm's members.");
        }

        // The query should include the unjoined ones, to show the invites.
        var query = db.RealmMembers
            .Where(m => m.RealmId == realm.Id)
            .Where(m => m.LeaveAt == null);

        if (withStatus)
        {
            var members = await query
                .OrderBy(m => m.JoinedAt)
                .ToListAsync();

            var memberStatuses = await accountEvents.GetStatuses(
                members.Select(m => m.AccountId).ToList()
            );

            members = members
                .Select(m =>
                {
                    m.Status = memberStatuses.TryGetValue(m.AccountId, out var s) ? s : null;
                    return m;
                })
                .OrderByDescending(m => m.Status?.IsOnline ?? false)
                .ToList();

            var total = members.Count;
            Response.Headers.Append("X-Total", total.ToString());

            var result = members.Skip(offset).Take(take).ToList();

            members = await rs.LoadMemberAccounts(result);

            return Ok(members.Where(m => m.Account is not null).ToList());
        }
        else
        {
            var total = await query.CountAsync();
            Response.Headers["X-Total"] = total.ToString();

            var members = await query
                .OrderBy(m => m.CreatedAt)
                .Skip(offset)
                .Take(take)
                .ToListAsync();
            members = await rs.LoadMemberAccounts(members);

            return Ok(members.Where(m => m.Account is not null).ToList());
        }
    }


    [HttpGet("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult<SnRealmMember>> GetCurrentIdentity(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (member is null) return NotFound();
        return Ok(await rs.LoadMemberAccount(member));
    }

    [HttpDelete("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult> LeaveRealm(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (member.Role == RealmMemberRole.DyOwner)
            return StatusCode(403, "Owner cannot leave their own realm.");

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.leave",
            new Dictionary<string, object>()
            {
                { "realm_id", member.RealmId.ToString() },
                { "account_id", member.AccountId.ToString() },
                { "leaver_id", currentUser.Id }
            },
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
    public async Task<ActionResult<SnRealm>> CreateRealm(RealmRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("You cannot create a realm without a name.");
        if (string.IsNullOrWhiteSpace(request.Slug)) return BadRequest("You cannot create a realm without a slug.");

        var slugExists = await db.Realms.AnyAsync(r => r.Slug == request.Slug);
        if (slugExists) return BadRequest("Realm with this slug already exists.");

        var realm = new SnRealm
        {
            Name = request.Name!,
            Slug = request.Slug!,
            Description = request.Description!,
            AccountId = currentUser.Id,
            IsCommunity = request.IsCommunity ?? false,
            IsPublic = request.IsPublic ?? false,
            Members = new List<SnRealmMember>
            {
                new()
                {
                    Role = RealmMemberRole.DyOwner,
                    AccountId = currentUser.Id,
                    JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        if (request.PictureId is not null)
        {
            var pictureResult = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
            if (pictureResult is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
            realm.Picture = SnCloudFileReferenceObject.FromProtoValue(pictureResult);
        }

        if (request.BackgroundId is not null)
        {
            var backgroundResult = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            if (backgroundResult is null) return BadRequest("Invalid background id, unable to find the file on cloud.");
            realm.Background = SnCloudFileReferenceObject.FromProtoValue(backgroundResult);
        }

        db.Realms.Add(realm);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.create",
            new Dictionary<string, object>()
            {
                { "realm_id", realm.Id.ToString() },
                { "name", realm.Name },
                { "slug", realm.Slug },
                { "is_community", realm.IsCommunity },
                { "is_public", realm.IsPublic }
            },
            Request
        );

        return Ok(realm);
    }

    [HttpPatch("{slug}")]
    [Authorize]
    public async Task<ActionResult<SnRealm>> Update(string slug, [FromBody] RealmRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var accountId = currentUser.Id;
        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId && m.RealmId == realm.Id && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null || member.Role < RealmMemberRole.DyModerator)
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
            var pictureResult = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
            if (pictureResult is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");

            realm.Picture = SnCloudFileReferenceObject.FromProtoValue(pictureResult);
        }

        if (request.BackgroundId is not null)
        {
            var backgroundResult = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            if (backgroundResult is null) return BadRequest("Invalid background id, unable to find the file on cloud.");

            realm.Background = SnCloudFileReferenceObject.FromProtoValue(backgroundResult);
        }

        db.Realms.Update(realm);
        await db.SaveChangesAsync();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.update",
            new Dictionary<string, object>()
            {
                { "realm_id", realm.Id.ToString() },
                { "name_updated", request.Name != null },
                { "slug_updated", request.Slug != null },
                { "description_updated", request.Description != null },
                { "picture_updated", request.PictureId != null },
                { "background_updated", request.BackgroundId != null },
                { "is_community_updated", request.IsCommunity != null },
                { "is_public_updated", request.IsPublic != null }
            },
            Request
        );

        return Ok(realm);
    }

    [HttpPost("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult<SnRealmMember>> JoinRealm(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

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
        {
            if (existingMember.LeaveAt == null)
                return BadRequest("You are already a member of this realm.");

            existingMember.LeaveAt = null;
            existingMember.JoinedAt = SystemClock.Instance.GetCurrentInstant();

            db.Update(existingMember);
            await db.SaveChangesAsync();

            als.CreateActionLogFromRequest(
                "realms.members.join",
                new Dictionary<string, object>()
                {
                    { "realm_id", existingMember.RealmId.ToString() },
                    { "account_id", currentUser.Id },
                    { "is_community", realm.IsCommunity }
                },
                Request
            );

            return Ok(existingMember);
        }

        var member = new SnRealmMember
        {
            AccountId = currentUser.Id,
            RealmId = realm.Id,
            Role = RealmMemberRole.DyNormal,
            JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        db.RealmMembers.Add(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.join",
            new Dictionary<string, object>()
            {
                { "realm_id", realm.Id.ToString() },
                { "account_id", currentUser.Id },
                { "is_community", realm.IsCommunity }
            },
            Request
        );

        return Ok(member);
    }

    [HttpDelete("{slug}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveMember(string slug, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var member = await db.RealmMembers
            .Where(m => m.AccountId == memberId && m.RealmId == realm.Id && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.DyModerator, member.Role))
            return StatusCode(403, "You do not have permission to remove members from this realm.");

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.kick",
            new Dictionary<string, object>()
            {
                { "realm_id", realm.Id.ToString() },
                { "account_id", memberId.ToString() },
                { "kicker_id", currentUser.Id }
            },
            Request
        );

        return NoContent();
    }

    [HttpPatch("{slug}/members/{memberId:guid}/role")]
    [Authorize]
    public async Task<ActionResult<SnRealmMember>> UpdateMemberRole(string slug, Guid memberId, [FromBody] int newRole)
    {
        if (newRole >= RealmMemberRole.DyOwner) return BadRequest("Unable to set realm member to owner or greater role.");
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var member = await db.RealmMembers
            .Where(m => m.AccountId == memberId && m.RealmId == realm.Id && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.DyModerator, member.Role,
                newRole))
            return StatusCode(403, "You do not have permission to update member roles in this realm.");

        member.Role = newRole;
        db.RealmMembers.Update(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.role_update",
            new Dictionary<string, object>()
            {
                { "realm_id", realm.Id.ToString() },
                { "account_id", memberId.ToString() },
                { "new_role", newRole },
                { "updater_id", currentUser.Id }
            },
            Request
        );

        return Ok(member);
    }

    [HttpDelete("{slug}")]
    [Authorize]
    public async Task<ActionResult> Delete(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var transaction = await db.Database.BeginTransactionAsync();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.DyOwner))
            return StatusCode(403, "Only the owner can delete this realm.");

        try
        {
            db.Realms.Remove(realm);
            await db.SaveChangesAsync();

            var now = SystemClock.Instance.GetCurrentInstant();
            await db.RealmMembers
                .Where(m => m.RealmId == realm.Id)
                .ExecuteUpdateAsync(m => m.SetProperty(m => m.DeletedAt, now));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }

        als.CreateActionLogFromRequest(
            "realms.delete",
            new Dictionary<string, object>()
            {
                { "realm_id", realm.Id.ToString() },
                { "realm_name", realm.Name },
                { "realm_slug", realm.Slug }
            },
            Request
        );

        return NoContent();
    }
}
