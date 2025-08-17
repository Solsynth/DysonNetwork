using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DysonNetwork.Sphere.Realm;

[ApiController]
[Route("/api/realms")]
public class RealmController(
    AppDatabase db,
    RealmService rs,
    FileService.FileServiceClient files,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    ActionLogService.ActionLogServiceClient als,
    AccountService.AccountServiceClient accounts,
    AccountClientHelper accountsHelper
) : Controller
{
    [HttpGet("{slug}")]
    public async Task<ActionResult<Realm>> GetRealm(string slug)
    {
        var realm = await db.Realms
            .Where(e => e.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        return Ok(realm);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Realm>>> ListJoinedRealms()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var members = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

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
    public async Task<ActionResult<RealmMember>> InviteMember(string slug,
        [FromBody] RealmMemberRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var relatedUser =
            await accounts.GetAccountAsync(new GetAccountRequest { Id = request.RelatedUserId.ToString() });
        if (relatedUser == null) return BadRequest("Related user was not found");

        var hasBlocked = await accounts.HasRelationshipAsync(new GetRelationshipRequest()
        {
            AccountId = currentUser.Id,
            RelatedId = request.RelatedUserId.ToString(),
            Status = -100
        });
        if (hasBlocked?.Value ?? false)
            return StatusCode(403, "You cannot invite a user that blocked you.");

        var realm = await db.Realms
            .Where(p => p.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, accountId, request.Role))
            return StatusCode(403, "You cannot invite member has higher permission than yours.");

        var hasExistingMember = await db.RealmMembers
            .Where(m => m.AccountId == Guid.Parse(relatedUser.Id))
            .Where(m => m.RealmId == realm.Id)
            .Where(m => m.LeaveAt == null)
            .AnyAsync();
        if (hasExistingMember)
            return BadRequest("This user has been joined the realm or leave cannot be invited again.");

        var member = new RealmMember
        {
            AccountId = Guid.Parse(relatedUser.Id),
            RealmId = realm.Id,
            Role = request.Role,
        };

        db.RealmMembers.Add(member);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "realms.members.invite",
            Meta =
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "account_id", Value.ForString(member.AccountId.ToString()) },
                { "role", Value.ForNumber(request.Role) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        member.AccountId = Guid.Parse(relatedUser.Id);
        member.Realm = realm;
        await rs.SendInviteNotify(member);

        return Ok(member);
    }

    [HttpPost("invites/{slug}/accept")]
    [Authorize]
    public async Task<ActionResult<Realm>> AcceptMemberInvite(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        db.Update(member);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "realms.members.join",
            Meta =
            {
                { "realm_id", Value.ForString(member.RealmId.ToString()) },
                { "account_id", Value.ForString(member.AccountId.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return Ok(member);
    }

    [HttpPost("invites/{slug}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineMemberInvite(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "realms.members.decline_invite",
            Meta =
            {
                { "realm_id", Value.ForString(member.RealmId.ToString()) },
                { "account_id", Value.ForString(member.AccountId.ToString()) },
                { "decliner_id", Value.ForString(currentUser.Id) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return NoContent();
    }


    [HttpGet("{slug}/members")]
    public async Task<ActionResult<List<RealmMember>>> ListMembers(
        string slug,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] bool withStatus = false,
        [FromQuery] string? status = null
    )
    {
        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!realm.IsPublic)
        {
            if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
            if (!await rs.IsMemberWithRole(realm.Id, Guid.Parse(currentUser.Id), RealmMemberRole.Normal))
                return StatusCode(403, "You must be a member to view this realm's members.");
        }

        var query = db.RealmMembers
            .Where(m => m.RealmId == realm.Id)
            .Where(m => m.LeaveAt == null);

        if (withStatus)
        {
            var members = await query
                .OrderBy(m => m.JoinedAt)
                .ToListAsync();

            var memberStatuses = await accountsHelper.GetAccountStatusBatch(
                members.Select(m => m.AccountId).ToList()
            );

            if (!string.IsNullOrEmpty(status))
            {
                members = members
                    .Select(m =>
                    {
                        m.Status = memberStatuses.TryGetValue(m.AccountId, out var s) ? s : null;
                        return m;
                    })
                    .ToList();
            }

            members = members
                .OrderByDescending(m => m.Status?.IsOnline ?? false)
                .ToList();

            var total = members.Count;
            Response.Headers.Append("X-Total", total.ToString());

            var result = members.Skip(offset).Take(take).ToList();

            return Ok(await rs.LoadMemberAccounts(result));
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
    public async Task<ActionResult<RealmMember>> GetCurrentIdentity(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .FirstOrDefaultAsync();

        if (member is null) return NotFound();
        return Ok(await rs.LoadMemberAccount(member));
    }

    [HttpDelete("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult> LeaveRealm(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt != null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (member.Role == RealmMemberRole.Owner)
            return StatusCode(403, "Owner cannot leave their own realm.");

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "realms.members.leave",
            Meta =
            {
                { "realm_id", Value.ForString(member.RealmId.ToString()) },
                { "account_id", Value.ForString(member.AccountId.ToString()) },
                { "leaver_id", Value.ForString(currentUser.Id) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("You cannot create a realm without a name.");
        if (string.IsNullOrWhiteSpace(request.Slug)) return BadRequest("You cannot create a realm without a slug.");

        var slugExists = await db.Realms.AnyAsync(r => r.Slug == request.Slug);
        if (slugExists) return BadRequest("Realm with this slug already exists.");

        var realm = new Realm
        {
            Name = request.Name!,
            Slug = request.Slug!,
            Description = request.Description!,
            AccountId = Guid.Parse(currentUser.Id),
            IsCommunity = request.IsCommunity ?? false,
            IsPublic = request.IsPublic ?? false,
            Members = new List<RealmMember>
            {
                new()
                {
                    Role = RealmMemberRole.Owner,
                    AccountId = Guid.Parse(currentUser.Id),
                    JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        if (request.PictureId is not null)
        {
            var pictureResult = await files.GetFileAsync(new GetFileRequest { Id = request.PictureId });
            if (pictureResult is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
            realm.Picture = CloudFileReferenceObject.FromProtoValue(pictureResult);
        }

        if (request.BackgroundId is not null)
        {
            var backgroundResult = await files.GetFileAsync(new GetFileRequest { Id = request.BackgroundId });
            if (backgroundResult is null) return BadRequest("Invalid background id, unable to find the file on cloud.");
            realm.Background = CloudFileReferenceObject.FromProtoValue(backgroundResult);
        }

        db.Realms.Add(realm);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "realms.create",
            Meta =
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "name", Value.ForString(realm.Name) },
                { "slug", Value.ForString(realm.Slug) },
                { "is_community", Value.ForBool(realm.IsCommunity) },
                { "is_public", Value.ForBool(realm.IsPublic) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        var realmResourceId = $"realm:{realm.Id}";

        if (realm.Picture is not null)
        {
            await fileRefs.CreateReferenceAsync(new CreateReferenceRequest
            {
                FileId = realm.Picture.Id,
                Usage = "realm.picture",
                ResourceId = realmResourceId
            });
        }

        if (realm.Background is not null)
        {
            await fileRefs.CreateReferenceAsync(new CreateReferenceRequest
            {
                FileId = realm.Background.Id,
                Usage = "realm.background",
                ResourceId = realmResourceId
            });
        }

        return Ok(realm);
    }

    [HttpPatch("{slug}")]
    [Authorize]
    public async Task<ActionResult<Realm>> Update(string slug, [FromBody] RealmRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId && m.RealmId == realm.Id && m.JoinedAt != null)
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
            var pictureResult = await files.GetFileAsync(new GetFileRequest { Id = request.PictureId });
            if (pictureResult is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");

            // Remove old references for the realm picture
            if (realm.Picture is not null)
            {
                await fileRefs.DeleteResourceReferencesAsync(new DeleteResourceReferencesRequest
                {
                    ResourceId = realm.ResourceIdentifier
                });
            }

            realm.Picture = CloudFileReferenceObject.FromProtoValue(pictureResult);

            // Create a new reference
            await fileRefs.CreateReferenceAsync(new CreateReferenceRequest
            {
                FileId = realm.Picture.Id,
                Usage = "realm.picture",
                ResourceId = realm.ResourceIdentifier
            });
        }

        if (request.BackgroundId is not null)
        {
            var backgroundResult = await files.GetFileAsync(new GetFileRequest { Id = request.BackgroundId });
            if (backgroundResult is null) return BadRequest("Invalid background id, unable to find the file on cloud.");

            // Remove old references for the realm background
            if (realm.Background is not null)
            {
                await fileRefs.DeleteResourceReferencesAsync(new DeleteResourceReferencesRequest
                {
                    ResourceId = realm.ResourceIdentifier
                });
            }

            realm.Background = CloudFileReferenceObject.FromProtoValue(backgroundResult);

            // Create a new reference
            await fileRefs.CreateReferenceAsync(new CreateReferenceRequest
            {
                FileId = realm.Background.Id,
                Usage = "realm.background",
                ResourceId = realm.ResourceIdentifier
            });
        }

        db.Realms.Update(realm);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "realms.update",
            Meta =
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "name_updated", Value.ForBool(request.Name != null) },
                { "slug_updated", Value.ForBool(request.Slug != null) },
                { "description_updated", Value.ForBool(request.Description != null) },
                { "picture_updated", Value.ForBool(request.PictureId != null) },
                { "background_updated", Value.ForBool(request.BackgroundId != null) },
                { "is_community_updated", Value.ForBool(request.IsCommunity != null) },
                { "is_public_updated", Value.ForBool(request.IsPublic != null) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return Ok(realm);
    }

    [HttpPost("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult<RealmMember>> JoinRealm(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!realm.IsCommunity)
            return StatusCode(403, "Only community realms can be joined without invitation.");

        var existingMember = await db.RealmMembers
            .Where(m => m.AccountId == Guid.Parse(currentUser.Id) && m.RealmId == realm.Id)
            .FirstOrDefaultAsync();
        if (existingMember is not null)
            return BadRequest("You are already a member of this realm.");

        var member = new RealmMember
        {
            AccountId = Guid.Parse(currentUser.Id),
            RealmId = realm.Id,
            Role = RealmMemberRole.Normal,
            JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        db.RealmMembers.Add(member);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "realms.members.join",
            Meta =
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "account_id", Value.ForString(currentUser.Id) },
                { "is_community", Value.ForBool(realm.IsCommunity) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return Ok(member);
    }

    [HttpDelete("{slug}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveMember(string slug, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var member = await db.RealmMembers
            .Where(m => m.AccountId == memberId && m.RealmId == realm.Id)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, Guid.Parse(currentUser.Id), RealmMemberRole.Moderator, member.Role))
            return StatusCode(403, "You do not have permission to remove members from this realm.");

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "realms.members.kick",
            Meta =
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "account_id", Value.ForString(memberId.ToString()) },
                { "kicker_id", Value.ForString(currentUser.Id) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return NoContent();
    }

    [HttpPatch("{slug}/members/{memberId:guid}/role")]
    [Authorize]
    public async Task<ActionResult<RealmMember>> UpdateMemberRole(string slug, Guid memberId, [FromBody] int newRole)
    {
        if (newRole >= RealmMemberRole.Owner) return BadRequest("Unable to set realm member to owner or greater role.");
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var member = await db.RealmMembers
            .Where(m => m.AccountId == memberId && m.RealmId == realm.Id)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, Guid.Parse(currentUser.Id), RealmMemberRole.Moderator, member.Role,
                newRole))
            return StatusCode(403, "You do not have permission to update member roles in this realm.");

        member.Role = newRole;
        db.RealmMembers.Update(member);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "realms.members.role_update",
            Meta =
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "account_id", Value.ForString(memberId.ToString()) },
                { "new_role", Value.ForNumber(newRole) },
                { "updater_id", Value.ForString(currentUser.Id) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        return Ok(member);
    }

    [HttpDelete("{slug}")]
    [Authorize]
    public async Task<ActionResult> Delete(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var transaction = await db.Database.BeginTransactionAsync();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, Guid.Parse(currentUser.Id), RealmMemberRole.Owner))
            return StatusCode(403, "Only the owner can delete this realm.");

        try
        {
            var chats = await db.ChatRooms
                .Where(c => c.RealmId == realm.Id)
                .Select(c => c.Id)
                .ToListAsync();

            db.Realms.Remove(realm);
            await db.SaveChangesAsync();

            var now = SystemClock.Instance.GetCurrentInstant();
            await db.RealmMembers
                .Where(m => m.RealmId == realm.Id)
                .ExecuteUpdateAsync(m => m.SetProperty(m => m.DeletedAt, now));
            await db.ChatRooms
                .Where(c => c.RealmId == realm.Id)
                .ExecuteUpdateAsync(c => c.SetProperty(c => c.DeletedAt, now));
            await db.ChatMessages
                .Where(m => chats.Contains(m.ChatRoomId))
                .ExecuteUpdateAsync(m => m.SetProperty(m => m.DeletedAt, now));
            await db.ChatMembers
                .Where(m => chats.Contains(m.ChatRoomId))
                .ExecuteUpdateAsync(m => m.SetProperty(m => m.DeletedAt, now));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "realms.delete",
            Meta =
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "realm_name", Value.ForString(realm.Name) },
                { "realm_slug", Value.ForString(realm.Slug) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        // Delete all file references for this realm
        var realmResourceId = $"realm:{realm.Id}";
        await fileRefs.DeleteResourceReferencesAsync(new DeleteResourceReferencesRequest
        {
            ResourceId = realmResourceId
        });

        return NoContent();
    }
}