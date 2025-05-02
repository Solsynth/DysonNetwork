using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Realm;

[ApiController]
[Route("/realms")]
public class RealmController(AppDatabase db, RealmService rs, FileService fs) : Controller
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
            .Include(e => e.Realm)
            .Include(e => e.Realm.Picture)
            .Include(e => e.Realm.Background)
            .Select(m => m.Realm)
            .ToListAsync();

        return members.ToList();
    }
    
    [HttpGet("/")]

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
            .Include(e => e.Realm.Picture)
            .Include(e => e.Realm.Background)
            .ToListAsync();

        return members.ToList();
    }

    public class RealmMemberRequest
    {
        [Required] public long RelatedUserId { get; set; }
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

        var realm = await db.Realms
            .Where(p => p.Slug == slug)
            .Include(publisher => publisher.Picture)
            .Include(publisher => publisher.Background)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var member = await db.RealmMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.RealmId == realm.Id)
            .FirstOrDefaultAsync();
        if (member is null) return StatusCode(403, "You are not even a member of the targeted realm.");
        if (member.Role < RealmMemberRole.Moderator)
            return StatusCode(403,
                "You need at least be a manager to invite other members to collaborate this realm.");
        if (member.Role < request.Role)
            return StatusCode(403, "You cannot invite member has higher permission than yours.");

        var newMember = new RealmMember
        {
            AccountId = relatedUser.Id,
            RealmId = realm.Id,
            Role = request.Role,
        };

        db.RealmMembers.Add(newMember);
        await db.SaveChangesAsync();

        return Ok(newMember);
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

        db.RealmMembers.Remove(member);
        await db.SaveChangesAsync();

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
        if (request.Name is null) return BadRequest("You cannot create a realm without a name.");
        if (request.Slug is null) return BadRequest("You cannot create a realm without a slug.");

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
                    AccountId = currentUser.Id
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

        if (realm.Picture is not null) await fs.MarkUsageAsync(realm.Picture, 1);
        if (realm.Background is not null) await fs.MarkUsageAsync(realm.Background, 1);

        return Ok(realm);
    }

    [HttpPut("{slug}")]
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
        return Ok(realm);
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

        var member = await db.RealmMembers
            .Where(m => m.AccountId == currentUser.Id && m.RealmId == realm.Id && m.JoinedAt != null)
            .FirstOrDefaultAsync();
        if (member is null || member.Role < RealmMemberRole.Owner)
            return StatusCode(403, "Only the owner can delete this realm.");

        db.Realms.Remove(realm);
        await db.SaveChangesAsync();

        if (realm.Picture is not null)
            await fs.MarkUsageAsync(realm.Picture, -1);
        if (realm.Background is not null)
            await fs.MarkUsageAsync(realm.Background, -1);

        return NoContent();
    }
}