using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

[ApiController]
[Route("/publishers")]
public class PublisherController(
    AppDatabase db,
    PublisherService ps,
    FileService fs,
    FileReferenceService fileRefService,
    ActionLogService als)
    : ControllerBase
{
    [HttpGet("{name}")]
    public async Task<ActionResult<Publisher>> GetPublisher(string name)
    {
        var publisher = await db.Publishers
            .Where(e => e.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();
        if (publisher.AccountId is null) return Ok(publisher);

        var account = await db.Accounts
            .Where(a => a.Id == publisher.AccountId)
            .Include(a => a.Profile)
            .FirstOrDefaultAsync();
        publisher.Account = account;

        return Ok(publisher);
    }

    [HttpGet("{name}/stats")]
    public async Task<ActionResult<PublisherService.PublisherStats>> GetPublisherStats(string name)
    {
        var stats = await ps.GetPublisherStats(name);
        if (stats is null) return NotFound();
        return Ok(stats);
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
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        if (!await ps.IsMemberWithRole(publisher.Id, currentUser.Id, request.Role))
            return StatusCode(403, "You cannot invite member has higher permission than yours.");

        var newMember = new PublisherMember
        {
            AccountId = relatedUser.Id,
            PublisherId = publisher.Id,
            Role = request.Role,
        };

        db.PublisherMembers.Add(newMember);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.PublisherMemberInvite,
            new Dictionary<string, object>
            {
                { "publisher_id", publisher.Id },
                { "account_id", relatedUser.Id }
            }, Request
        );

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

        als.CreateActionLogFromRequest(
            ActionLogType.PublisherMemberJoin,
            new Dictionary<string, object> { { "account_id", member.AccountId } }, Request
        );

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

        als.CreateActionLogFromRequest(
            ActionLogType.PublisherMemberLeave,
            new Dictionary<string, object> { { "account_id", member.AccountId } }, Request
        );

        return NoContent();
    }

    [HttpDelete("{name}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveMember(string name, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == memberId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound("Member was not found");
        if (!await ps.IsMemberWithRole(publisher.Id, currentUser.Id, PublisherMemberRole.Manager))
            return StatusCode(403, "You need at least be a manager to remove members from this publisher.");

        db.PublisherMembers.Remove(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.PublisherMemberKick,
            new Dictionary<string, object>
            {
                { "publisher_id", publisher.Id },
                { "account_id", memberId }
            }, Request
        );

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
    public async Task<ActionResult<Publisher>> CreatePublisherIndividual([FromBody] PublisherRequest request)
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

        als.CreateActionLogFromRequest(
            ActionLogType.PublisherCreate,
            new Dictionary<string, object> { { "publisher_id", publisher.Id } }, Request
        );

        return Ok(publisher);
    }

    [HttpPost("organization/{realmSlug}")]
    [Authorize]
    [RequiredPermission("global", "publishers.create")]
    public async Task<ActionResult<Publisher>> CreatePublisherOrganization(string realmSlug,
        [FromBody] PublisherRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var realm = await db.Realms.FirstOrDefaultAsync(r => r.Slug == realmSlug);
        if (realm == null) return NotFound("Realm not found");

        var isAdmin = await db.RealmMembers
            .AnyAsync(m =>
                m.RealmId == realm.Id && m.AccountId == currentUser.Id && m.Role >= RealmMemberRole.Moderator);
        if (!isAdmin)
            return StatusCode(403, "You need to be a moderator of the realm to create an organization publisher");

        var takenName = request.Name ?? realm.Slug;
        var duplicateNameCount = await db.Publishers
            .Where(p => p.Name == takenName)
            .CountAsync();
        if (duplicateNameCount > 0)
            return BadRequest("The name you requested has already been taken");

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

        var publisher = await ps.CreateOrganizationPublisher(
            realm,
            currentUser,
            request.Name,
            request.Nick,
            request.Bio,
            picture,
            background
        );

        als.CreateActionLogFromRequest(
            ActionLogType.PublisherCreate,
            new Dictionary<string, object> { { "publisher_id", publisher.Id } }, Request
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

            // Remove old references for the publisher picture
            if (publisher.Picture is not null)
            {
                var oldPictureRefs = await fileRefService.GetResourceReferencesAsync(
                    publisher.ResourceIdentifier,
                    "publisher.picture"
                );
                foreach (var oldRef in oldPictureRefs)
                    await fileRefService.DeleteReferenceAsync(oldRef.Id);
            }

            publisher.Picture = picture.ToReferenceObject();

            // Create a new reference
            await fileRefService.CreateReferenceAsync(
                picture.Id,
                "publisher.picture",
                publisher.ResourceIdentifier
            );
        }

        if (request.BackgroundId is not null)
        {
            var background = await db.Files.Where(f => f.Id == request.BackgroundId).FirstOrDefaultAsync();
            if (background is null) return BadRequest("Invalid background id.");

            var publisherResourceId = $"publisher:{publisher.Id}";

            // Remove old references for the publisher background
            if (publisher.Background is not null)
            {
                var oldBackgroundRefs =
                    await fileRefService.GetResourceReferencesAsync(publisherResourceId, "publisher.background");
                foreach (var oldRef in oldBackgroundRefs)
                {
                    await fileRefService.DeleteReferenceAsync(oldRef.Id);
                }
            }

            publisher.Background = background.ToReferenceObject();

            // Create a new reference
            await fileRefService.CreateReferenceAsync(
                background.Id,
                "publisher.background",
                publisherResourceId
            );
        }

        db.Update(publisher);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.PublisherUpdate,
            new Dictionary<string, object> { { "publisher_id", publisher.Id } }, Request
        );

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

        var publisherResourceId = $"publisher:{publisher.Id}";

        // Delete all file references for this publisher
        await fileRefService.DeleteResourceReferencesAsync(publisherResourceId);

        db.Publishers.Remove(publisher);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.PublisherDelete,
            new Dictionary<string, object> { { "publisher_id", publisher.Id } }, Request
        );

        return NoContent();
    }
}