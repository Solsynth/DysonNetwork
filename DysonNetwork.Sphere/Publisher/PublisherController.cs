using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Realm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

[ApiController]
[Route("/api/publishers")]
public class PublisherController(
    AppDatabase db,
    PublisherService ps,
    AccountService.AccountServiceClient accounts,
    FileService.FileServiceClient files,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    ActionLogService.ActionLogServiceClient als
)
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

        var account = await accounts.GetAccountAsync(
            new GetAccountRequest { Id = publisher.AccountId.Value.ToString() }
        );
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var members = await db.PublisherMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null)
            .Include(e => e.Publisher)
            .ToListAsync();

        return members.Select(m => m.Publisher).ToList();
    }

    [HttpGet("invites")]
    [Authorize]
    public async Task<ActionResult<List<PublisherMember>>> ListInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var members = await db.PublisherMembers
            .Where(m => m.AccountId == accountId)
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var relatedUser =
            await accounts.GetAccountAsync(new GetAccountRequest { Id = request.RelatedUserId.ToString() });
        if (relatedUser == null) return BadRequest("Related user was not found");

        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        if (!await ps.IsMemberWithRole(publisher.Id, accountId, request.Role))
            return StatusCode(403, "You cannot invite member has higher permission than yours.");

        var newMember = new PublisherMember
        {
            AccountId = Guid.Parse(relatedUser.Id),
            PublisherId = publisher.Id,
            Role = request.Role,
        };

        db.PublisherMembers.Add(newMember);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "publishers.members.invite",
            Meta =
            {
                { "publisher_id", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString()) },
                { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(relatedUser.Id.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(newMember);
    }

    [HttpPost("invites/{name}/accept")]
    [Authorize]
    public async Task<ActionResult<Publisher>> AcceptMemberInvite(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Publisher.Name == name)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        db.Update(member);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "publishers.members.join",
            Meta =
            {
                { "publisher_id", Google.Protobuf.WellKnownTypes.Value.ForString(member.PublisherId.ToString()) },
                { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(member.AccountId.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(member);
    }

    [HttpPost("invites/{name}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineMemberInvite(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Publisher.Name == name)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        db.PublisherMembers.Remove(member);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "publishers.members.decline",
            Meta =
            {
                { "publisher_id", Google.Protobuf.WellKnownTypes.Value.ForString(member.PublisherId.ToString()) },
                { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(member.AccountId.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return NoContent();
    }

    [HttpDelete("{name}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveMember(string name, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == memberId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();
        var accountId = Guid.Parse(currentUser.Id);
        if (member is null) return NotFound("Member was not found");
        if (!await ps.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Manager))
            return StatusCode(403, "You need at least be a manager to remove members from this publisher.");

        db.PublisherMembers.Remove(member);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "publishers.members.kick",
            Meta =
            {
                { "publisher_id", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString()) },
                { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(memberId.ToString()) },
                { "kicked_by", Google.Protobuf.WellKnownTypes.Value.ForString(currentUser.Id) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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

        CloudFileReferenceObject? picture = null, background = null;
        if (request.PictureId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new GetFileRequest { Id = request.PictureId }
            );
            if (queryResult is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            picture = CloudFileReferenceObject.FromProtoValue(queryResult);
        }

        if (request.BackgroundId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new GetFileRequest { Id = request.BackgroundId }
            );
            if (queryResult is null)
                throw new InvalidOperationException("Invalid background id, unable to find the file on cloud.");
            background = CloudFileReferenceObject.FromProtoValue(queryResult);
        }

        var publisher = await ps.CreateIndividualPublisher(
            currentUser,
            request.Name,
            request.Nick,
            request.Bio,
            picture,
            background
        );

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "publishers.create",
            Meta =
            {
                { "publisher_id", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString()) },
                { "publisher_name", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Name) },
                { "publisher_type", Google.Protobuf.WellKnownTypes.Value.ForString("Individual") }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(publisher);
    }

    [HttpPost("organization/{realmSlug}")]
    [Authorize]
    [RequiredPermission("global", "publishers.create")]
    public async Task<ActionResult<Publisher>> CreatePublisherOrganization(string realmSlug,
        [FromBody] PublisherRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var realm = await db.Realms.FirstOrDefaultAsync(r => r.Slug == realmSlug);
        if (realm == null) return NotFound("Realm not found");

        var accountId = Guid.Parse(currentUser.Id);
        var isAdmin = await db.RealmMembers
            .AnyAsync(m =>
                m.RealmId == realm.Id && m.AccountId == accountId && m.Role >= RealmMemberRole.Moderator);
        if (!isAdmin)
            return StatusCode(403, "You need to be a moderator of the realm to create an organization publisher");

        var takenName = request.Name ?? realm.Slug;
        var duplicateNameCount = await db.Publishers
            .Where(p => p.Name == takenName)
            .CountAsync();
        if (duplicateNameCount > 0)
            return BadRequest("The name you requested has already been taken");

        CloudFileReferenceObject? picture = null, background = null;
        if (request.PictureId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new GetFileRequest { Id = request.PictureId }
            );
            if (queryResult is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            picture = CloudFileReferenceObject.FromProtoValue(queryResult);
        }

        if (request.BackgroundId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new GetFileRequest { Id = request.BackgroundId }
            );
            if (queryResult is null)
                throw new InvalidOperationException("Invalid background id, unable to find the file on cloud.");
            background = CloudFileReferenceObject.FromProtoValue(queryResult);
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

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "publishers.create",
            Meta =
            {
                { "publisher_id", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString()) },
                { "publisher_name", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Name) },
                { "publisher_type", Google.Protobuf.WellKnownTypes.Value.ForString("Organization") },
                { "realm_slug", Google.Protobuf.WellKnownTypes.Value.ForString(realm.Slug) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(publisher);
    }


    [HttpPatch("{name}")]
    [Authorize]
    public async Task<ActionResult<Publisher>> UpdatePublisher(string name, PublisherRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == accountId)
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
            var queryResult = await files.GetFileAsync(
                new GetFileRequest { Id = request.PictureId }
            );
            if (queryResult is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            var picture = CloudFileReferenceObject.FromProtoValue(queryResult);

            // Remove old references for the publisher picture
            if (publisher.Picture is not null)
                await fileRefs.DeleteResourceReferencesAsync(new DeleteResourceReferencesRequest
                {
                    ResourceId = publisher.ResourceIdentifier
                });

            publisher.Picture = picture;

            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    FileId = picture.Id,
                    Usage = "publisher.picture",
                    ResourceId = publisher.ResourceIdentifier
                }
            );
        }

        if (request.BackgroundId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new GetFileRequest { Id = request.BackgroundId }
            );
            if (queryResult is null)
                throw new InvalidOperationException("Invalid background id, unable to find the file on cloud.");
            var background = CloudFileReferenceObject.FromProtoValue(queryResult);

            // Remove old references for the publisher background
            if (publisher.Background is not null)
            {
                await fileRefs.DeleteResourceReferencesAsync(new DeleteResourceReferencesRequest
                {
                    ResourceId = publisher.ResourceIdentifier
                });
            }

            publisher.Background = background;

            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    FileId = background.Id,
                    Usage = "publisher.background",
                    ResourceId = publisher.ResourceIdentifier
                }
            );
        }

        db.Update(publisher);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "publishers.update",
            Meta =
            {
                { "publisher_id", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString()) },
                { "name_updated", Google.Protobuf.WellKnownTypes.Value.ForBool(!string.IsNullOrEmpty(request.Name)) },
                { "nick_updated", Google.Protobuf.WellKnownTypes.Value.ForBool(!string.IsNullOrEmpty(request.Nick)) },
                { "bio_updated", Google.Protobuf.WellKnownTypes.Value.ForBool(!string.IsNullOrEmpty(request.Bio)) },
                { "picture_updated", Google.Protobuf.WellKnownTypes.Value.ForBool(request.PictureId != null) },
                { "background_updated", Google.Protobuf.WellKnownTypes.Value.ForBool(request.BackgroundId != null) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(publisher);
    }

    [HttpDelete("{name}")]
    [Authorize]
    public async Task<ActionResult<Publisher>> DeletePublisher(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();
        if (member is null) return StatusCode(403, "You are not even a member of the targeted publisher.");
        if (member.Role < PublisherMemberRole.Owner)
            return StatusCode(403, "You need to be the owner to delete the publisher.");

        var publisherResourceId = $"publisher:{publisher.Id}";

        // Delete all file references for this publisher
        await fileRefs.DeleteResourceReferencesAsync(
            new DeleteResourceReferencesRequest { ResourceId = publisherResourceId }
        );

        db.Publishers.Remove(publisher);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "publishers.delete",
            Meta =
            {
                { "publisher_id", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString()) },
                { "publisher_name", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Name) },
                { "publisher_type", Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Type.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return NoContent();
    }

    [HttpGet("{name}/members")]
    public async Task<ActionResult<List<PublisherMember>>> ListMembers(
        string name,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var query = db.PublisherMembers
            .Where(m => m.PublisherId == publisher.Id)
            .Where(m => m.JoinedAt != null);

        var total = await query.CountAsync();
        Response.Headers["X-Total"] = total.ToString();

        var members = await query
            .OrderBy(m => m.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(members);
    }

    [HttpGet("{name}/members/me")]
    [Authorize]
    public async Task<ActionResult<PublisherMember>> GetCurrentIdentity(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var member = await db.PublisherMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();

        if (member is null) return NotFound();
        return Ok(member);
    }

    [HttpGet("{name}/features")]
    [Authorize]
    public async Task<ActionResult<Dictionary<string, bool>>> ListPublisherFeatures(string name)
    {
        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var features = await db.PublisherFeatures
            .Where(f => f.PublisherId == publisher.Id)
            .ToListAsync();

        var dict = PublisherFeatureFlag.AllFlags.ToDictionary(
            flag => flag,
            _ => false
        );

        foreach (
            var feature in features.Where(feature =>
                feature.ExpiredAt == null || !(feature.ExpiredAt < SystemClock.Instance.GetCurrentInstant())
            )
        )
        {
            dict[feature.Flag] = true;
        }

        return Ok(dict);
    }

    public class PublisherFeatureRequest
    {
        [Required] public string Flag { get; set; } = null!;
        public Instant? ExpiredAt { get; set; }
    }

    [HttpPost("{name}/features")]
    [Authorize]
    [RequiredPermission("maintenance", "publishers.features")]
    public async Task<ActionResult<PublisherFeature>> AddPublisherFeature(string name,
        [FromBody] PublisherFeatureRequest request)
    {
        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var feature = new PublisherFeature
        {
            PublisherId = publisher.Id,
            Flag = request.Flag,
            ExpiredAt = request.ExpiredAt
        };

        db.PublisherFeatures.Add(feature);
        await db.SaveChangesAsync();

        return Ok(feature);
    }

    [HttpDelete("{name}/features/{flag}")]
    [Authorize]
    [RequiredPermission("maintenance", "publishers.features")]
    public async Task<ActionResult> RemovePublisherFeature(string name, string flag)
    {
        var publisher = await db.Publishers
            .Where(p => p.Name == name)
            .FirstOrDefaultAsync();
        if (publisher is null) return NotFound();

        var feature = await db.PublisherFeatures
            .Where(f => f.PublisherId == publisher.Id)
            .Where(f => f.Flag == flag)
            .FirstOrDefaultAsync();
        if (feature is null) return NotFound();

        db.PublisherFeatures.Remove(feature);
        await db.SaveChangesAsync();

        return NoContent();
    }
}