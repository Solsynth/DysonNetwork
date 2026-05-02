using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.ActivityPub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;

namespace DysonNetwork.Sphere.Publisher;

[ApiController]
[Route("/api/publishers")]
public class PublisherController(
    AppDatabase db,
    PublisherService ps,
    PublisherQuotaService quotaService,
    PublisherRatingService ratingService,
    DyAccountService.DyAccountServiceClient accounts,
    DyFileService.DyFileServiceClient files,
    RemoteActionLogService als,
    RemoteRealmService remoteRealmService,
    IServiceScopeFactory factory,
    RemoteAccountService remoteAccounts,
    ILogger<PublisherController> logger
) : ControllerBase
{
    [HttpGet("quota")]
    [Authorize]
    public async Task<ActionResult<ResourceQuotaResponse<PublisherQuotaRecord>>> GetQuota()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var account = SnAccount.FromProtoValue(
            await remoteAccounts.GetAccount(Guid.Parse(currentUser.Id))
        );
        return Ok(await quotaService.GetQuotaAsync(account));
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnPublisher>>> ListManagedPublishers()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var members = await db
            .PublisherMembers.Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null)
            .Include(e => e.Publisher)
            .ToListAsync();

        var publishers = members.Select(m => m.Publisher).ToList();
        publishers = await ps.HydratePublisherRealm(publishers);

        return publishers;
    }

    [HttpGet("invites")]
    [Authorize]
    public async Task<ActionResult<List<SnPublisherMember>>> ListInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var members = await db
            .PublisherMembers.Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt == null)
            .Include(e => e.Publisher)
            .ToListAsync();

        var result = await ps.LoadMemberAccounts(members);
        result = await ps.HydrateMemberPublisherRealms(result);
        return result;
    }

    public class PublisherMemberRequest
    {
        [Required]
        public Guid RelatedUserId { get; set; }

        [Required]
        public PublisherMemberRole Role { get; set; }
    }

    [HttpPost("invites/{name}")]
    [Authorize]
    public async Task<ActionResult<SnPublisherMember>> InviteMember(
        string name,
        [FromBody] PublisherMemberRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var relatedUser = await accounts.GetAccountAsync(
            new DyGetAccountRequest { Id = request.RelatedUserId.ToString() }
        );
        if (relatedUser == null)
            return BadRequest("Related user was not found");

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        if (!await ps.IsMemberWithRole(publisher.Id, accountId, request.Role))
            return StatusCode(403, "You cannot invite member has higher permission than yours.");

        var newMember = new SnPublisherMember
        {
            AccountId = Guid.Parse(relatedUser.Id),
            PublisherId = publisher.Id,
            Role = request.Role,
        };

        db.PublisherMembers.Add(newMember);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            "publishers.members.invite",
            new Dictionary<string, object>
            {
                { "publisher_id", publisher.Id.ToString() },
                { "account_id", relatedUser.Id.ToString() },
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(newMember);
    }

    [HttpPost("invites/{name}/accept")]
    [Authorize]
    public async Task<ActionResult<SnPublisher>> AcceptMemberInvite(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var member = await db
            .PublisherMembers.Where(m => m.AccountId == accountId)
            .Where(m => m.Publisher.Name == name)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null)
            return NotFound();

        member.JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        db.Update(member);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            "publishers.members.join",
            new Dictionary<string, object>
            {
                { "publisher_id", member.PublisherId.ToString() },
                { "account_id", member.AccountId.ToString() },
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        var publisher = await ps.HydratePublisherRealm([member.Publisher]);
        return Ok(publisher.First());
    }

    [HttpPost("invites/{name}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineMemberInvite(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var member = await db
            .PublisherMembers.Where(m => m.AccountId == accountId)
            .Where(m => m.Publisher.Name == name)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null)
            return NotFound();

        db.PublisherMembers.Remove(member);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            "publishers.members.decline",
            new Dictionary<string, object>
            {
                { "publisher_id", member.PublisherId.ToString() },
                { "account_id", member.AccountId.ToString() },
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return NoContent();
    }

    [HttpDelete("{name}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveMember(string name, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var member = await db
            .PublisherMembers.Where(m => m.AccountId == memberId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();
        var accountId = Guid.Parse(currentUser.Id);
        if (member is null)
            return NotFound("Member was not found");
        if (!await ps.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Manager))
            return StatusCode(
                403,
                "You need at least be a manager to remove members from this publisher."
            );

        db.PublisherMembers.Remove(member);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            "publishers.members.kick",
            new Dictionary<string, object>
            {
                { "publisher_id", publisher.Id.ToString() },
                { "account_id", memberId.ToString() },
                { "kicked_by", currentUser.Id },
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return NoContent();
    }

    [HttpPatch("{name}/members/{memberId:guid}/role")]
    [Authorize]
    public async Task<ActionResult<SnPublisherMember>> UpdateMemberRole(
        string name,
        Guid memberId,
        [FromBody] int newRole
    )
    {
        if (newRole >= (int)PublisherMemberRole.Owner)
            return BadRequest("Unable to set publisher member to owner or greater role.");
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var member = await db
            .PublisherMembers.Where(m =>
                m.AccountId == memberId && m.PublisherId == publisher.Id && m.JoinedAt != null
            )
            .FirstOrDefaultAsync();
        if (member is null)
            return NotFound();

        var requiredRole = Math.Max(
            (int)PublisherMemberRole.Manager,
            Math.Max((int)member.Role, newRole)
        );
        if (
            !await ps.IsMemberWithRole(
                publisher.Id,
                Guid.Parse(currentUser.Id),
                (PublisherMemberRole)requiredRole
            )
        )
            return StatusCode(
                403,
                "You do not have permission to update member roles in this publisher."
            );

        member.Role = (PublisherMemberRole)newRole;
        db.PublisherMembers.Update(member);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            "publishers.members.role_update",
            new Dictionary<string, object>
            {
                { "publisher_id", publisher.Id.ToString() },
                { "account_id", memberId.ToString() },
                { "new_role", newRole },
                { "updater_id", currentUser.Id },
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(member);
    }

    public class PublisherRequest
    {
        [RegularExpression(
            @"^[a-zA-Z0-9](?:[a-zA-Z0-9\-_\.]*[a-zA-Z0-9])?$",
            ErrorMessage = "Name must be URL-safe (alphanumeric, hyphens, underscores, or periods) and cannot start/end with special characters."
        )]
        [MaxLength(256)]
        public string? Name { get; set; }

        [MaxLength(256)]
        public string? Nick { get; set; }

        [MaxLength(4096)]
        public string? Bio { get; set; }

        public string? PictureId { get; set; }
        public string? BackgroundId { get; set; }
        public List<string>? DefaultPostTags { get; set; }
        public List<string>? DefaultPostCategories { get; set; }
    }

    [HttpPost("individual")]
    [Authorize]
    [AskPermission("publishers.create")]
    public async Task<ActionResult<SnPublisher>> CreatePublisherIndividual(
        [FromBody] PublisherRequest request
    )
    {
        if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Nick))
            return BadRequest("Name and Nick are required.");
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var hydratedAccount = SnAccount.FromProtoValue(
            await remoteAccounts.GetAccount(Guid.Parse(currentUser.Id))
        );
        var quota = await quotaService.GetQuotaAsync(hydratedAccount);
        if (quota.Used >= quota.Total)
            return StatusCode(403, $"Publisher quota exceeded ({quota.Used}/{quota.Total}).");

        var takenName = request.Name ?? currentUser.Name;
        var duplicateNameCount = await db.Publishers.Where(p => p.Name == takenName).CountAsync();
        if (duplicateNameCount > 0)
            return BadRequest(
                "The name you requested has already be taken, "
                    + "if it is your account name, "
                    + "you can request a taken down to the publisher which created with "
                    + "your name firstly to get your name back."
            );

        SnCloudFileReferenceObject? picture = null,
            background = null;
        if (request.PictureId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new DyGetFileRequest { Id = request.PictureId }
            );
            if (queryResult is null)
                throw new InvalidOperationException(
                    "Invalid picture id, unable to find the file on cloud."
                );
            picture = SnCloudFileReferenceObject.FromProtoValue(queryResult);
        }

        if (request.BackgroundId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new DyGetFileRequest { Id = request.BackgroundId }
            );
            if (queryResult is null)
                throw new InvalidOperationException(
                    "Invalid background id, unable to find the file on cloud."
                );
            background = SnCloudFileReferenceObject.FromProtoValue(queryResult);
        }

        var publisher = await ps.CreateIndividualPublisher(
            currentUser,
            request.Name,
            request.Nick,
            request.Bio,
            picture,
            background
        );

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            "publishers.create",
            new Dictionary<string, object>
            {
                { "publisher_id", publisher.Id.ToString() },
                { "publisher_name", publisher.Name },
                { "publisher_type", "Individual" },
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        publisher = (await ps.HydratePublisherRealm([publisher])).First();

        return Ok(publisher);
    }

    [HttpPost("organization/{realmSlug}")]
    [Authorize]
    [AskPermission("publishers.create")]
    public async Task<ActionResult<SnPublisher>> CreatePublisherOrganization(
        string realmSlug,
        [FromBody] PublisherRequest request
    )
    {
        if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Nick))
            return BadRequest("Name and Nick are required.");
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var hydratedAccount = SnAccount.FromProtoValue(
            await remoteAccounts.GetAccount(Guid.Parse(currentUser.Id))
        );
        var quota = await quotaService.GetQuotaAsync(hydratedAccount);
        if (quota.Used >= quota.Total)
            return StatusCode(403, $"Publisher quota exceeded ({quota.Used}/{quota.Total}).");

        var realm = await remoteRealmService.GetRealmBySlug(realmSlug);
        if (realm == null)
            return NotFound("Realm not found");

        var accountId = Guid.Parse(currentUser.Id);
        var isAdmin = await remoteRealmService.IsMemberWithRole(
            realm.Id,
            accountId,
            [RealmMemberRole.Moderator]
        );
        if (!isAdmin)
            return StatusCode(
                403,
                "You need to be a moderator of the realm to create an organization publisher"
            );

        var takenName = request.Name ?? realm.Slug;
        var duplicateNameCount = await db.Publishers.Where(p => p.Name == takenName).CountAsync();
        if (duplicateNameCount > 0)
            return BadRequest("The name you requested has already been taken");

        SnCloudFileReferenceObject? picture = null,
            background = null;
        if (request.PictureId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new DyGetFileRequest { Id = request.PictureId }
            );
            if (queryResult is null)
                throw new InvalidOperationException(
                    "Invalid picture id, unable to find the file on cloud."
                );
            picture = SnCloudFileReferenceObject.FromProtoValue(queryResult);
        }

        if (request.BackgroundId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new DyGetFileRequest { Id = request.BackgroundId }
            );
            if (queryResult is null)
                throw new InvalidOperationException(
                    "Invalid background id, unable to find the file on cloud."
                );
            background = SnCloudFileReferenceObject.FromProtoValue(queryResult);
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

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            "publishers.create",
            new Dictionary<string, object>
            {
                { "publisher_id", publisher.Id.ToString() },
                { "publisher_name", publisher.Name },
                { "publisher_type", "Organization" },
                { "realm_slug", realm.Slug },
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        publisher.Realm = realm;
        return Ok(publisher);
    }

    [HttpPatch("{name}")]
    [Authorize]
    public async Task<ActionResult<SnPublisher>> UpdatePublisher(
        string name,
        PublisherRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var member = await db
            .PublisherMembers.Where(m => m.AccountId == accountId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();
        if (member is null)
            return StatusCode(403, "You are not even a member of the targeted publisher.");
        if (member.Role < PublisherMemberRole.Manager)
            return StatusCode(
                403,
                "You need at least be the manager to update the publisher profile."
            );

        if (request.Name is not null)
            publisher.Name = request.Name;
        if (request.Nick is not null)
            publisher.Nick = request.Nick;
        if (request.Bio is not null)
            publisher.Bio = request.Bio;
        if (request.PictureId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new DyGetFileRequest { Id = request.PictureId }
            );
            if (queryResult is null)
                throw new InvalidOperationException(
                    "Invalid picture id, unable to find the file on cloud."
                );
            var picture = SnCloudFileReferenceObject.FromProtoValue(queryResult);

            publisher.Picture = picture;
        }

        if (request.BackgroundId is not null)
        {
            var queryResult = await files.GetFileAsync(
                new DyGetFileRequest { Id = request.BackgroundId }
            );
            if (queryResult is null)
                throw new InvalidOperationException(
                    "Invalid background id, unable to find the file on cloud."
                );
            var background = SnCloudFileReferenceObject.FromProtoValue(queryResult);

            publisher.Background = background;
        }

        if (request.DefaultPostTags is not null || request.DefaultPostCategories is not null)
        {
            publisher.Meta ??= new Dictionary<string, object>();

            if (request.DefaultPostTags is not null)
                publisher.Meta["default_post_tags"] = request
                    .DefaultPostTags.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToList();

            if (request.DefaultPostCategories is not null)
                publisher.Meta["default_post_categories"] = request
                    .DefaultPostCategories.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToList();
        }

        db.Update(publisher);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            "publishers.update",
            new Dictionary<string, object>
            {
                { "publisher_id", publisher.Id.ToString() },
                { "name_updated", !string.IsNullOrEmpty(request.Name) },
                { "nick_updated", !string.IsNullOrEmpty(request.Nick) },
                { "bio_updated", !string.IsNullOrEmpty(request.Bio) },
                { "picture_updated", request.PictureId != null },
                { "background_updated", request.BackgroundId != null },
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        // Send ActivityPub Update activity if actor exists
        var actor = await db.FediverseActors.FirstOrDefaultAsync(a =>
            a.PublisherId == publisher.Id
        );

        if (actor != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = factory.CreateScope();
                    var deliveryService =
                        scope.ServiceProvider.GetRequiredService<ActivityPubDeliveryService>();
                    await deliveryService.SendUpdateActorActivityAsync(actor);
                }
                catch (Exception ex)
                {
                    using var errorScope = factory.CreateScope();
                    var errorLogger = errorScope.ServiceProvider.GetRequiredService<
                        ILogger<ActivityPubDeliveryService>
                    >();
                    errorLogger.LogError(
                        ex,
                        "Error sending ActivityPub Update actor activity for publisher {PublisherId}",
                        publisher.Id
                    );
                }
            });
        }

        publisher = (await ps.HydratePublisherRealm([publisher])).First();
        return Ok(publisher);
    }

    [HttpDelete("{name}")]
    [Authorize]
    public async Task<ActionResult<SnPublisher>> DeletePublisher(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var member = await db
            .PublisherMembers.Where(m => m.AccountId == accountId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();
        if (member is null)
            return StatusCode(403, "You are not even a member of the targeted publisher.");
        if (member.Role < PublisherMemberRole.Owner)
            return StatusCode(403, "You need to be the owner to delete the publisher.");

        var publisherResourceId = $"publisher:{publisher.Id}";

        db.Publishers.Remove(publisher);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            "publishers.delete",
            new Dictionary<string, object>
            {
                { "publisher_id", publisher.Id.ToString() },
                { "publisher_name", publisher.Name },
                { "publisher_type", publisher.Type.ToString() },
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return NoContent();
    }

    [HttpGet("{name}/members")]
    public async Task<ActionResult<List<SnPublisherMember>>> ListMembers(
        string name,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var query = db
            .PublisherMembers.Where(m => m.PublisherId == publisher.Id)
            .Where(m => m.JoinedAt != null);

        var total = await query.CountAsync();
        Response.Headers["X-Total"] = total.ToString();

        var members = await query.OrderBy(m => m.CreatedAt).Skip(offset).Take(take).ToListAsync();
        members = await ps.LoadMemberAccounts(members);
        members = await ps.HydrateMemberPublisherRealms(members);

        return Ok(members.Where(m => m.Account is not null).ToList());
    }

    [HttpGet("{name}/members/me")]
    [Authorize]
    public async Task<ActionResult<SnPublisherMember>> GetCurrentIdentity(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var member = await db
            .PublisherMembers.Where(m => m.AccountId == accountId)
            .Where(m => m.PublisherId == publisher.Id)
            .FirstOrDefaultAsync();

        if (member is null)
            return NotFound();

        var result = await ps.LoadMemberAccount(member);
        await ps.HydrateMemberPublisherRealms([result]);
        return Ok(result);
    }

    [HttpGet("{name}/features")]
    [Authorize]
    public async Task<ActionResult<Dictionary<string, bool>>> ListPublisherFeatures(string name)
    {
        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var dict = new Dictionary<string, bool>
        {
            [PublisherFeatureFlag.FollowRequiresApproval] = publisher.IsModerateSubscription,
            [PublisherFeatureFlag.PostsRequireFollow] = publisher.IsGatekept,
        };

        return Ok(dict);
    }

    [HttpGet("{name}/rewards")]
    [Authorize]
    public async Task<
        ActionResult<PublisherService.PublisherRewardPreview>
    > GetPublisherExpectedReward(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        if (!await ps.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Viewer))
            return StatusCode(403, "You are not allowed to view stats data of this publisher.");

        var result = await ps.GetPublisherExpectedReward(publisher.Id);
        return Ok(result);
    }

    [HttpGet("{name}/rating/history")]
    [Authorize]
    public async Task<ActionResult<List<SnPublisherRatingRecord>>> GetPublisherRatingHistory(
        string name,
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        if (!await ps.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Viewer))
            return StatusCode(403, "You are not allowed to view rating data of this publisher.");

        var total = await ratingService.GetRatingHistoryCount(publisher.Id);
        HttpContext.Response.Headers["X-Total"] = total.ToString();

        var records = await ratingService.GetRatingHistory(publisher.Id, take, offset);
        return Ok(records);
    }

    public class PublisherFeatureRequest
    {
        [Required]
        public string Flag { get; set; } = null!;
        public Instant? ExpiredAt { get; set; }
    }

    [HttpPost("{name}/features")]
    [Authorize]
    public async Task<ActionResult<SnPublisherFeature>> AddPublisherFeature(
        string name,
        [FromBody] PublisherFeatureRequest request
    )
    {
        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        if (PublisherFeatureFlag.SystemOnlyFlags.Contains(request.Flag))
            return BadRequest(
                $"Flag '{request.Flag}' is a system flag and cannot be enabled manually"
            );

        if (request.Flag == PublisherFeatureFlag.FollowRequiresApproval)
        {
            if (publisher.AccountId.HasValue)
            {
                var publisherAccount = await remoteAccounts.GetAccount(publisher.AccountId.Value);
                if (
                    publisherAccount != null
                    && publisherAccount.PerkLevel < PublisherFeatureFlag.MinimumPerkLevel
                )
                    return StatusCode(
                        403,
                        $"This feature requires PerkLevel >= {PublisherFeatureFlag.MinimumPerkLevel}"
                    );
            }
            publisher.ModerateSubscription = true;
            db.Publishers.Update(publisher);
            await db.SaveChangesAsync();
            return Ok(new SnPublisherFeature { PublisherId = publisher.Id, Flag = request.Flag });
        }

        if (request.Flag == PublisherFeatureFlag.PostsRequireFollow)
        {
            if (publisher.AccountId.HasValue)
            {
                var publisherAccount = await remoteAccounts.GetAccount(publisher.AccountId.Value);
                if (
                    publisherAccount != null
                    && publisherAccount.PerkLevel < PublisherFeatureFlag.MinimumPerkLevel
                )
                    return StatusCode(
                        403,
                        $"This feature requires PerkLevel >= {PublisherFeatureFlag.MinimumPerkLevel}"
                    );
            }
            publisher.GatekeptFollows = true;
            db.Publishers.Update(publisher);
            await db.SaveChangesAsync();
            return Ok(new SnPublisherFeature { PublisherId = publisher.Id, Flag = request.Flag });
        }

        var feature = new SnPublisherFeature
        {
            PublisherId = publisher.Id,
            Flag = request.Flag,
            ExpiredAt = request.ExpiredAt,
        };

        db.PublisherFeatures.Add(feature);
        await db.SaveChangesAsync();

        return Ok(feature);
    }

    [HttpDelete("{name}/features")]
    [Authorize]
    public async Task<ActionResult> RemovePublisherFeature(
        string name,
        [FromQuery(Name = "flag")] string? flag
    )
    {
        if (string.IsNullOrEmpty(flag))
            return BadRequest("Flag is required");

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        if (flag == PublisherFeatureFlag.FollowRequiresApproval)
        {
            publisher.ModerateSubscription = null;
            db.Publishers.Update(publisher);
            await db.SaveChangesAsync();
            return NoContent();
        }

        if (flag == PublisherFeatureFlag.PostsRequireFollow)
        {
            publisher.GatekeptFollows = null;
            db.Publishers.Update(publisher);
            await db.SaveChangesAsync();
            return NoContent();
        }

        var feature = await db
            .PublisherFeatures.Where(f => f.PublisherId == publisher.Id)
            .Where(f => f.Flag == flag)
            .FirstOrDefaultAsync();
        if (feature is null)
            return NotFound();

        db.PublisherFeatures.Remove(feature);
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("rewards/settle")]
    [Authorize]
    [AskPermission("publishers.reward.settle")]
    public async Task<IActionResult> SettlePublisherAward()
    {
        await ps.SettlePublisherRewards();
        return Ok();
    }

    public class AggressiveResettleRequest
    {
        public Instant DateFrom { get; set; }
        public Instant DateTo { get; set; }
        public Guid? PublisherId { get; set; }
    }

    [HttpPost("rewards/resettle")]
    [Authorize]
    [AskPermission("publishers.reward.settle")]
    public async Task<IActionResult> AggressiveResettle([FromBody] AggressiveResettleRequest request)
    {
        var results = await ps.AggressiveResettle(
            request.DateFrom,
            request.DateTo,
            request.PublisherId
        );

        return Ok(new
        {
            processed = results.Count,
            publishers = results.Select(r => new { publisherId = r.Key, delta = r.Value }).ToList()
        });
    }

    [HttpGet("{name}/fediverse")]
    [Authorize]
    public async Task<ActionResult<FediverseStatus>> GetFediverseStatus(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var status = await ps.GetFediverseStatusAsync(publisher.Id, Guid.Parse(currentUser.Id));
        return Ok(status);
    }

    [HttpPost("{name}/fediverse")]
    [Authorize]
    public async Task<ActionResult<FediverseStatus>> EnableFediverse(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        if (!await ps.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Manager))
            return StatusCode(
                403,
                "You need at least be manager to enable fediverse for this publisher."
            );

        try
        {
            var actor = await ps.EnableFediverseAsync(publisher.Id, accountId);
            var status = await ps.GetFediverseStatusAsync(publisher.Id);
            return Ok(status);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{name}/fediverse")]
    [Authorize]
    public async Task<ActionResult> DisableFediverse(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        if (!await ps.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Manager))
            return StatusCode(
                403,
                "You need at least be manager to disable fediverse for this publisher."
            );

        try
        {
            await ps.DisableFediverseAsync(publisher.Id, accountId);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }
    }
}
