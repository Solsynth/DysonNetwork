using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Realm;

[ApiController]
[Route("/realms")]
public class RealmController : Controller
{
    private readonly AppDatabase _db;
    private readonly RealmService _realmService;
    private readonly FileService _fileService;

    public RealmController(AppDatabase db, RealmService rs, FileService fs)
    {
        _db = db;
        _realmService = rs;
        _fileService = fs;
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<Realm>> GetRealm(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var realm = await _db.Realms
            .Where(e => e.Name == name)
            .Include(e => e.Picture)
            .Include(e => e.Background)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        return Ok(realm);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Realm>>> ListManagedRealms()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var members = await _db.RealmMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.JoinedAt != null)
            .Include(e => e.Realm)
            .Include(e => e.Realm.Picture)
            .Include(e => e.Realm.Background)
            .ToListAsync();

        return members.Select(m => m.Realm).ToList();
    }

    [HttpGet("invites")]
    [Authorize]
    public async Task<ActionResult<List<RealmMember>>> ListInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var members = await _db.RealmMembers
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

    [HttpPost("invites/{name}")]
    [Authorize]
    public async Task<ActionResult<RealmMember>> InviteMember(string name,
        [FromBody] RealmMemberRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var relatedUser = await _db.Accounts.FindAsync(request.RelatedUserId);
        if (relatedUser is null) return BadRequest("Related user was not found");

        var realm = await _db.Realms
            .Where(p => p.Name == name)
            .Include(publisher => publisher.Picture)
            .Include(publisher => publisher.Background)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var member = await _db.RealmMembers
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

        _db.RealmMembers.Add(newMember);
        await _db.SaveChangesAsync();

        return Ok(newMember);
    }

    [HttpPost("invites/{name}/accept")]
    [Authorize]
    public async Task<ActionResult<Realm>> AcceptMemberInvite(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var member = await _db.RealmMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.Realm.Name == name)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        _db.Update(member);
        await _db.SaveChangesAsync();

        return Ok(member);
    }

    [HttpPost("invites/{name}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineMemberInvite(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var member = await _db.RealmMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.Realm.Name == name)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        _db.RealmMembers.Remove(member);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    public class RealmRequest
    {
        [MaxLength(1024)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        public string? PictureId { get; set; }
        public string? BackgroundId { get; set; }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<Realm>> CreateRealm(RealmRequest request)
    {
         if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var realm = new Realm();
        realm.Name = request.Name!;
        realm.Description = request.Description!;

        if (request.PictureId is not null)
        {
           realm.Picture = await _db.Files.FindAsync(request.PictureId);
        }

        if (request.BackgroundId is not null)
        {
            realm.Background = await _db.Files.FindAsync(request.BackgroundId);
        }
        
        var result = await _realmService.CreateRealmAsync(realm, currentUser);

        return Ok(result);
    }

}
