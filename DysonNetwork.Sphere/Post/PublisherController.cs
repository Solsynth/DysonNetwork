using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/publishers")]
public class PublisherController(AppDatabase db, PublisherService ps, FileService fs)
    : ControllerBase
{
    [HttpGet("{name}")]
    public async Task<ActionResult<Publisher>> GetPublisher(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var publisher = await db.Publishers
            .Where(e => e.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        return Ok(publisher);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Publisher>>> ListManagedPublishers()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var members = await db.PublisherMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.JoinedAt != null)
            .Include(e => e.Publisher)
            .Include(e => e.Publisher.Picture)
            .Include(e => e.Publisher.Background)
            .ToListAsync();

        return members.Select(m => m.Publisher).ToList();
    }

    [HttpGet("invites")]
    [Authorize]
    public async Task<ActionResult<List<PublisherMember>>> ListInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var members = await db.PublisherMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.JoinedAt == null)
            .Include(e => e.Publisher)
            .Include(e => e.Publisher.Picture)
            .Include(e => e.Publisher.Background)
            .ToListAsync();

        return members.ToList();
    }

    public class PublisherMemberRequest
    {
        [Required] public long RelatedUserId { get; set; }
        [Required] public PublisherMemberRole Role { get; set; }
    }

    [HttpPost("invites/{name}")]
    [Authorize]
    public async Task<ActionResult<PublisherMember>> InviteMember(string name,
        [FromBody] PublisherMemberRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var relatedUser = await db.Accounts.FindAsync(request.RelatedUserId);
        if (relatedUser is null) return BadRequest("Related user was not found");

        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .Include(publisher => publisher.Picture)
            .Include(publisher => publisher.Background)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();
        if (member is null) return StatusCode(403, "You are not even a member of the targeted publisher.");
        if (member.Role < PublisherMemberRole.Manager)
            return StatusCode(403,
                "You need at least be a manager to invite other members to collaborate this publisher.");
        if (member.Role < request.Role)
            return StatusCode(403, "You cannot invite member has higher permission than yours.");

        var newMember = new PublisherMember
        {
            AccountId = relatedUser.Id,
            PublisherId = publisher.Id,
            Role = request.Role,
        };

        db.PublisherMembers.Add(newMember);
        await db.SaveChangesAsync();

        return Ok(newMember);
    }

    [HttpPost("invites/{name}/accept")]
    [Authorize]
    public async Task<ActionResult<Publisher>> AcceptMemberInvite(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.Publisher.Name == name)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        db.Update(member);
        await db.SaveChangesAsync();

        return Ok(member);
    }

    [HttpPost("invites/{name}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineMemberInvite(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.Publisher.Name == name)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        db.PublisherMembers.Remove(member);
        await db.SaveChangesAsync();

        return NoContent();
    }

    public class PublisherRequest
    {
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(256)] public string? Nick { get; set; }
        [MaxLength(4096)] public string? Bio { get; set; }

        public string? PictureId { get; set; }
        public string? BackgroundId { get; set; }
    }

    [HttpPost("individual")]
    [Authorize]
    [RequiredPermission("global", "publishers.create")]
    public async Task<ActionResult<Publisher>> CreatePublisherIndividual(PublisherRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var takenName = request.Name ?? currentUser.Name;
        var duplicateNameCount = await db.Publishers
            .Where(p => p.Name == takenName)
            .CountAsync();
        if (duplicateNameCount > 0)
            return BadRequest(
                "The name you requested has already be taken, " +
                "if it is your account name, " +
                "you can request a taken down to the publisher which created with " +
                "your name firstly to get your name back."
            );

        CloudFile? picture = null, background = null;
        if (request.PictureId is not null)
        {
            picture = await db.Files.Where(f => f.Id == request.PictureId).FirstOrDefaultAsync();
            if (picture is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
        }

        if (request.BackgroundId is not null)
        {
            background = await db.Files.Where(f => f.Id == request.BackgroundId).FirstOrDefaultAsync();
            if (background is null) return BadRequest("Invalid background id, unable to find the file on cloud.");
        }

        var publisher = await ps.CreateIndividualPublisher(
            currentUser,
            request.Name,
            request.Nick,
            request.Bio,
            picture,
            background
        );

        return Ok(publisher);
    }

    [HttpPatch("{name}")]
    [Authorize]
    public async Task<ActionResult<Publisher>> UpdatePublisher(string name, PublisherRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .Include(publisher => publisher.Picture)
            .Include(publisher => publisher.Background)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();
        if (member is null) return StatusCode(403, "You are not even a member of the targeted publisher.");
        if (member.Role < PublisherMemberRole.Manager)
            return StatusCode(403, "You need at least be the manager to update the publisher profile.");

        if (request.Name is not null) publisher.Name = request.Name;
        if (request.Nick is not null) publisher.Nick = request.Nick;
        if (request.Bio is not null) publisher.Bio = request.Bio;
        if (request.PictureId is not null)
        {
            var picture = await db.Files.Where(f => f.Id == request.PictureId).FirstOrDefaultAsync();
            if (picture is null) return BadRequest("Invalid picture id.");
            if (publisher.Picture is not null) await fs.MarkUsageAsync(publisher.Picture, -1);

            publisher.Picture = picture;
            await fs.MarkUsageAsync(picture, 1);
        }

        if (request.BackgroundId is not null)
        {
            var background = await db.Files.Where(f => f.Id == request.BackgroundId).FirstOrDefaultAsync();
            if (background is null) return BadRequest("Invalid background id.");
            if (publisher.Background is not null) await fs.MarkUsageAsync(publisher.Background, -1);

            publisher.Background = background;
            await fs.MarkUsageAsync(background, 1);
        }

        db.Update(publisher);
        await db.SaveChangesAsync();

        return Ok(publisher);
    }

    [HttpDelete("{name}")]
    [Authorize]
    public async Task<ActionResult<Publisher>> DeletePublisher(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .Include(publisher => publisher.Picture)
            .Include(publisher => publisher.Background)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();
        if (member is null) return StatusCode(403, "You are not even a member of the targeted publisher.");
        if (member.Role < PublisherMemberRole.Owner)
            return StatusCode(403, "You need to be the owner to delete the publisher.");

        if (publisher.Picture is not null)
            await fs.MarkUsageAsync(publisher.Picture, -1);
        if (publisher.Background is not null)
            await fs.MarkUsageAsync(publisher.Background, -1);

        db.Publishers.Remove(publisher);
        await db.SaveChangesAsync();

        return NoContent();
    }
}