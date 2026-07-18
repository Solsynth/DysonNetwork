using System.ComponentModel.DataAnnotations;
using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using ActionLogService = DysonNetwork.Passport.Account.ActionLogService;

namespace DysonNetwork.Passport.Realm;

[ApiController]
[Route("/api/admin/realms")]
[Authorize]
[ApiFeature("admin.realms", Revision = 1)]
[ApiFeature("admin.realms.members", Revision = 1)]
public class RealmAdminController(
    AppDatabase db,
    RealmService realmService,
    ActionLogService als
) : ControllerBase
{
    public class AdminUpdateRealmRequest
    {
        [MaxLength(1024)] public string? Slug { get; set; }
        [MaxLength(1024)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        public bool? IsCommunity { get; set; }
        public bool? IsPublic { get; set; }
        public Guid? AccountId { get; set; }
    }

    public class SetRealmVerificationRequest
    {
        public VerificationMarkType Type { get; set; }
        [MaxLength(1024)] public string? Title { get; set; }
        [MaxLength(8192)] public string? Description { get; set; }
        [MaxLength(1024)] public string? VerifiedBy { get; set; }
    }

    public class UpdateMemberRoleRequest
    {
        public int Role { get; set; }
    }

    public class AdminRealmDetail
    {
        public SnRealm Realm { get; set; } = null!;
        public int MemberCount { get; set; }
        public int PendingInviteCount { get; set; }
        public int LabelCount { get; set; }
        public int ActiveBoostContributionCount { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.RealmsModerate)]
    public async Task<ActionResult<List<SnRealm>>> ListRealms(
        [FromQuery] string? query = null,
        [FromQuery] bool? isPublic = null,
        [FromQuery] bool? isCommunity = null,
        [FromQuery] bool? verified = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var realmsQuery = db.Realms.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var probe = query.Trim();
            realmsQuery = realmsQuery.Where(r =>
                EF.Functions.ILike(r.Slug, $"%{probe}%") ||
                EF.Functions.ILike(r.Name, $"%{probe}%") ||
                EF.Functions.ILike(r.Description, $"%{probe}%"));
        }

        if (isPublic.HasValue)
            realmsQuery = realmsQuery.Where(r => r.IsPublic == isPublic.Value);
        if (isCommunity.HasValue)
            realmsQuery = realmsQuery.Where(r => r.IsCommunity == isCommunity.Value);
        if (accountId.HasValue)
            realmsQuery = realmsQuery.Where(r => r.AccountId == accountId.Value);
        if (verified == true)
            realmsQuery = realmsQuery.Where(r => r.Verification != null);
        if (verified == false)
            realmsQuery = realmsQuery.Where(r => r.Verification == null);

        var total = await realmsQuery.CountAsync(HttpContext.RequestAborted);
        Response.Headers.Append("X-Total", total.ToString());

        var realms = await realmsQuery
            .OrderByDescending(r => r.UpdatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync(HttpContext.RequestAborted);

        await realmService.RefreshBoostStates(realms, HttpContext.RequestAborted);
        return Ok(realms);
    }

    [HttpGet("{slug}")]
    [AskPermission(PermissionKeys.RealmsModerate)]
    public async Task<ActionResult<AdminRealmDetail>> GetRealm(string slug)
    {
        var realm = await db.Realms
            .FirstOrDefaultAsync(r => r.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (realm is null)
            return NotFound(new ApiError { Code = "PASSPORT_REALM_NOT_FOUND", Message = "Realm not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        await realmService.RefreshBoostState(realm, HttpContext.RequestAborted);

        var cutoff = RealmBoostPolicy.GetActiveCutoff(SystemClock.Instance.GetCurrentInstant());
        var detail = new AdminRealmDetail
        {
            Realm = realm,
            MemberCount = await db.RealmMembers.CountAsync(
                m => m.RealmId == realm.Id && m.JoinedAt != null && m.LeaveAt == null,
                HttpContext.RequestAborted
            ),
            PendingInviteCount = await db.RealmMembers.CountAsync(
                m => m.RealmId == realm.Id && m.JoinedAt == null && m.LeaveAt == null,
                HttpContext.RequestAborted
            ),
            LabelCount = await db.RealmLabels.CountAsync(
                l => l.RealmId == realm.Id,
                HttpContext.RequestAborted
            ),
            ActiveBoostContributionCount = await db.RealmBoostContributions.CountAsync(
                c => c.RealmId == realm.Id && c.CreatedAt >= cutoff,
                HttpContext.RequestAborted
            )
        };

        return Ok(detail);
    }

    [HttpPatch("{slug}")]
    [AskPermission(PermissionKeys.RealmsUpdate)]
    public async Task<ActionResult<SnRealm>> UpdateRealm(
        string slug,
        [FromBody] AdminUpdateRealmRequest request
    )
    {
        var realm = await db.Realms
            .FirstOrDefaultAsync(r => r.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (realm is null)
            return NotFound(new ApiError { Code = "PASSPORT_REALM_NOT_FOUND", Message = "Realm not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        if (request.Slug is not null)
        {
            var normalized = request.Slug.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return BadRequest(new ApiError { Code = "PASSPORT_REALM_SLUG_REQUIRED", Message = "Slug cannot be empty.", Status = 400, TraceId = HttpContext.TraceIdentifier });

            if (!string.Equals(normalized, realm.Slug, StringComparison.OrdinalIgnoreCase))
            {
                var exists = await db.Realms.AnyAsync(
                    r => r.Slug.ToLower() == normalized.ToLowerInvariant() && r.Id != realm.Id,
                    HttpContext.RequestAborted
                );
                if (exists)
                    return BadRequest(new ApiError { Code = "PASSPORT_REALM_SLUG_EXISTS", Message = "A realm with this slug already exists.", Status = 400, TraceId = HttpContext.TraceIdentifier });
                realm.Slug = normalized;
            }
        }

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new ApiError { Code = "PASSPORT_REALM_NAME_REQUIRED", Message = "Name cannot be empty.", Status = 400, TraceId = HttpContext.TraceIdentifier });
            realm.Name = request.Name.Trim();
        }

        if (request.Description is not null)
            realm.Description = request.Description;
        if (request.IsCommunity.HasValue)
            realm.IsCommunity = request.IsCommunity.Value;
        if (request.IsPublic.HasValue)
            realm.IsPublic = request.IsPublic.Value;
        if (request.AccountId.HasValue)
            realm.AccountId = request.AccountId.Value;

        await db.SaveChangesAsync(HttpContext.RequestAborted);
        await realmService.RefreshBoostState(realm, HttpContext.RequestAborted);

        als.CreateActionLogFromRequest(
            ActionLogType.RealmUpdate,
            new Dictionary<string, object>
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "operation", Value.ForString("admin_update") },
                { "slug", Value.ForString(realm.Slug) }
            },
            Request
        );

        return Ok(realm);
    }

    [HttpPost("{slug}/verification")]
    [AskPermission(PermissionKeys.RealmsModerate)]
    public async Task<ActionResult<SnRealm>> SetVerification(
        string slug,
        [FromBody] SetRealmVerificationRequest request
    )
    {
        var realm = await db.Realms
            .FirstOrDefaultAsync(r => r.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (realm is null)
            return NotFound(new ApiError { Code = "PASSPORT_REALM_NOT_FOUND", Message = "Realm not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        realm.Verification = new SnVerificationMark
        {
            Type = request.Type,
            Title = request.Title,
            Description = request.Description,
            VerifiedBy = request.VerifiedBy
        };
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        als.CreateActionLogFromRequest(
            ActionLogType.RealmsModerate,
            new Dictionary<string, object>
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "operation", Value.ForString("set_verification") },
                { "verification_type", Value.ForString(request.Type.ToString()) }
            },
            Request
        );

        return Ok(realm);
    }

    [HttpDelete("{slug}/verification")]
    [AskPermission(PermissionKeys.RealmsModerate)]
    public async Task<ActionResult<SnRealm>> ClearVerification(string slug)
    {
        var realm = await db.Realms
            .FirstOrDefaultAsync(r => r.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (realm is null)
            return NotFound(new ApiError { Code = "PASSPORT_REALM_NOT_FOUND", Message = "Realm not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        realm.Verification = null;
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        als.CreateActionLogFromRequest(
            ActionLogType.RealmsModerate,
            new Dictionary<string, object>
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "operation", Value.ForString("clear_verification") }
            },
            Request
        );

        return Ok(realm);
    }

    [HttpGet("{slug}/members")]
    [AskPermission(PermissionKeys.RealmsModerate)]
    public async Task<ActionResult<List<SnRealmMember>>> ListMembers(
        string slug,
        [FromQuery] int? role = null,
        [FromQuery] bool pendingOnly = false,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var realm = await db.Realms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (realm is null)
            return NotFound(new ApiError { Code = "PASSPORT_REALM_NOT_FOUND", Message = "Realm not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        var query = db.RealmMembers
            .AsNoTracking()
            .Include(m => m.Label)
            .Where(m => m.RealmId == realm.Id);

        if (pendingOnly)
            query = query.Where(m => m.JoinedAt == null && m.LeaveAt == null);
        else
            query = query.Where(m => m.JoinedAt != null && m.LeaveAt == null);

        if (role.HasValue)
            query = query.Where(m => m.Role == role.Value);

        var total = await query.CountAsync(HttpContext.RequestAborted);
        Response.Headers.Append("X-Total", total.ToString());

        var members = await query
            .OrderByDescending(m => m.Role)
            .ThenByDescending(m => m.Experience)
            .ThenBy(m => m.JoinedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync(HttpContext.RequestAborted);

        members = await realmService.LoadMemberAccounts(members);
        return Ok(members);
    }

    [HttpPatch("{slug}/members/{memberId:guid}/role")]
    [AskPermission(PermissionKeys.RealmsMembersManage)]
    public async Task<ActionResult<SnRealmMember>> UpdateMemberRole(
        string slug,
        Guid memberId,
        [FromBody] UpdateMemberRoleRequest request
    )
    {
        var realm = await db.Realms
            .FirstOrDefaultAsync(r => r.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (realm is null)
            return NotFound(new ApiError { Code = "PASSPORT_REALM_NOT_FOUND", Message = "Realm not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        var member = await db.RealmMembers
            .FirstOrDefaultAsync(
                m => m.RealmId == realm.Id && m.AccountId == memberId && m.JoinedAt != null && m.LeaveAt == null,
                HttpContext.RequestAborted
            );
        if (member is null)
            return NotFound(new ApiError { Code = "PASSPORT_REALM_MEMBER_NOT_FOUND", Message = "Realm member not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        // Admin may promote/demote freely, including transfer-level ownership.
        member.Role = request.Role;
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        als.CreateActionLogFromRequest(
            ActionLogType.RealmsModerate,
            new Dictionary<string, object>
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "operation", Value.ForString("set_member_role") },
                { "account_id", Value.ForString(memberId.ToString()) },
                { "role", Value.ForNumber(request.Role) }
            },
            Request
        );

        return Ok(await realmService.LoadMemberAccount(member));
    }

    [HttpDelete("{slug}/members/{memberId:guid}")]
    [AskPermission(PermissionKeys.RealmsMembersManage)]
    public async Task<IActionResult> RemoveMember(string slug, Guid memberId)
    {
        var realm = await db.Realms
            .FirstOrDefaultAsync(r => r.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (realm is null)
            return NotFound(new ApiError { Code = "PASSPORT_REALM_NOT_FOUND", Message = "Realm not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        var member = await db.RealmMembers
            .FirstOrDefaultAsync(
                m => m.RealmId == realm.Id && m.AccountId == memberId && m.LeaveAt == null,
                HttpContext.RequestAborted
            );
        if (member is null)
            return NotFound(new ApiError { Code = "PASSPORT_REALM_MEMBER_NOT_FOUND", Message = "Realm member not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        als.CreateActionLogFromRequest(
            ActionLogType.RealmKick,
            new Dictionary<string, object>
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "operation", Value.ForString("admin_kick") },
                { "account_id", Value.ForString(memberId.ToString()) }
            },
            Request
        );

        return NoContent();
    }

    [HttpDelete("{slug}")]
    [AskPermission(PermissionKeys.RealmsDelete)]
    public async Task<IActionResult> DeleteRealm(string slug)
    {
        var realm = await db.Realms
            .FirstOrDefaultAsync(r => r.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (realm is null)
            return NotFound(new ApiError { Code = "PASSPORT_REALM_NOT_FOUND", Message = "Realm not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        var realmId = realm.Id;
        var realmName = realm.Name;
        var realmSlug = realm.Slug;

        await using var transaction = await db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        try
        {
            db.Realms.Remove(realm);
            await db.SaveChangesAsync(HttpContext.RequestAborted);

            var now = SystemClock.Instance.GetCurrentInstant();
            await db.RealmMembers
                .Where(m => m.RealmId == realmId)
                .ExecuteUpdateAsync(
                    m => m.SetProperty(x => x.DeletedAt, now).SetProperty(x => x.LeaveAt, now),
                    HttpContext.RequestAborted
                );

            await transaction.CommitAsync(HttpContext.RequestAborted);
        }
        catch
        {
            await transaction.RollbackAsync(HttpContext.RequestAborted);
            throw;
        }

        als.CreateActionLogFromRequest(
            ActionLogType.RealmDelete,
            new Dictionary<string, object>
            {
                { "realm_id", Value.ForString(realmId.ToString()) },
                { "operation", Value.ForString("admin_delete") },
                { "realm_name", Value.ForString(realmName) },
                { "realm_slug", Value.ForString(realmSlug) }
            },
            Request
        );

        return NoContent();
    }
}
