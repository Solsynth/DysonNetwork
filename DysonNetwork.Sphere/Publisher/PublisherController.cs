using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
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
    DyAccountService.DyAccountServiceClient accounts,
    DyFileService.DyFileServiceClient files,
    DyActionLogService.DyActionLogServiceClient als,
    RemoteRealmService remoteRealmService,
    IServiceScopeFactory factory
) : ControllerBase
{
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

        return members.Select(m => m.Publisher).ToList();
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

        return await ps.LoadMemberAccounts(members);
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

        _ = als.CreateActionLogAsync(
            new DyCreateActionLogRequest
            {
                Action = "publishers.members.invite",
                Meta =
                {
                    {
                        "publisher_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString())
                    },
                    {
                        "account_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(relatedUser.Id.ToString())
                    },
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
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

        _ = als.CreateActionLogAsync(
            new DyCreateActionLogRequest
            {
                Action = "publishers.members.join",
                Meta =
                {
                    {
                        "publisher_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(
                            member.PublisherId.ToString()
                        )
                    },
                    {
                        "account_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(member.AccountId.ToString())
                    },
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return Ok(member);
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

        _ = als.CreateActionLogAsync(
            new DyCreateActionLogRequest
            {
                Action = "publishers.members.decline",
                Meta =
                {
                    {
                        "publisher_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(
                            member.PublisherId.ToString()
                        )
                    },
                    {
                        "account_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(member.AccountId.ToString())
                    },
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
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
        if (
            !await ps.IsMemberWithRole(
                publisher.Id,
                accountId,
                PublisherMemberRole.Manager
            )
        )
            return StatusCode(
                403,
                "You need at least be a manager to remove members from this publisher."
            );

        db.PublisherMembers.Remove(member);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(
            new DyCreateActionLogRequest
            {
                Action = "publishers.members.kick",
                Meta =
                {
                    {
                        "publisher_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString())
                    },
                    {
                        "account_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(memberId.ToString())
                    },
                    { "kicked_by", Google.Protobuf.WellKnownTypes.Value.ForString(currentUser.Id) },
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return NoContent();
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

        _ = als.CreateActionLogAsync(
            new DyCreateActionLogRequest
            {
                Action = "publishers.create",
                Meta =
                {
                    {
                        "publisher_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString())
                    },
                    {
                        "publisher_name",
                        Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Name)
                    },
                    {
                        "publisher_type",
                        Google.Protobuf.WellKnownTypes.Value.ForString("Individual")
                    },
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

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

        _ = als.CreateActionLogAsync(
            new DyCreateActionLogRequest
            {
                Action = "publishers.create",
                Meta =
                {
                    {
                        "publisher_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString())
                    },
                    {
                        "publisher_name",
                        Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Name)
                    },
                    {
                        "publisher_type",
                        Google.Protobuf.WellKnownTypes.Value.ForString("Organization")
                    },
                    { "realm_slug", Google.Protobuf.WellKnownTypes.Value.ForString(realm.Slug) },
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

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

        db.Update(publisher);
        await db.SaveChangesAsync();

        _ = als.CreateActionLogAsync(
            new DyCreateActionLogRequest
            {
                Action = "publishers.update",
                Meta =
                {
                    {
                        "publisher_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString())
                    },
                    {
                        "name_updated",
                        Google.Protobuf.WellKnownTypes.Value.ForBool(
                            !string.IsNullOrEmpty(request.Name)
                        )
                    },
                    {
                        "nick_updated",
                        Google.Protobuf.WellKnownTypes.Value.ForBool(
                            !string.IsNullOrEmpty(request.Nick)
                        )
                    },
                    {
                        "bio_updated",
                        Google.Protobuf.WellKnownTypes.Value.ForBool(
                            !string.IsNullOrEmpty(request.Bio)
                        )
                    },
                    {
                        "picture_updated",
                        Google.Protobuf.WellKnownTypes.Value.ForBool(request.PictureId != null)
                    },
                    {
                        "background_updated",
                        Google.Protobuf.WellKnownTypes.Value.ForBool(request.BackgroundId != null)
                    },
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        // Send ActivityPub Update activity if actor exists
        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.PublisherId == publisher.Id);

        if (actor != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = factory.CreateScope();
                    var deliveryService = scope.ServiceProvider.GetRequiredService<ActivityPubDeliveryService>();
                    await deliveryService.SendUpdateActorActivityAsync(actor);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error sending ActivityPub Update actor activity for publisher {publisher.Id}: {ex.Message}");
                }
            });
        }

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

        _ = als.CreateActionLogAsync(
            new DyCreateActionLogRequest
            {
                Action = "publishers.delete",
                Meta =
                {
                    {
                        "publisher_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Id.ToString())
                    },
                    {
                        "publisher_name",
                        Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Name)
                    },
                    {
                        "publisher_type",
                        Google.Protobuf.WellKnownTypes.Value.ForString(publisher.Type.ToString())
                    },
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
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
        return Ok(await ps.LoadMemberAccount(member));
    }

    [HttpGet("{name}/features")]
    [Authorize]
    public async Task<ActionResult<Dictionary<string, bool>>> ListPublisherFeatures(string name)
    {
        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var features = await db
            .PublisherFeatures.Where(f => f.PublisherId == publisher.Id)
            .ToListAsync();

        var dict = PublisherFeatureFlag.AllFlags.ToDictionary(flag => flag, _ => false);

        foreach (
            var feature in features.Where(feature =>
                feature.ExpiredAt == null
                || !(feature.ExpiredAt < SystemClock.Instance.GetCurrentInstant())
            )
        )
        {
            dict[feature.Flag] = true;
        }

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

        if (!await ps.IsMemberWithRole(publisher.Id, accountId,PublisherMemberRole.Viewer))
            return StatusCode(403, "You are not allowed to view stats data of this publisher.");

        var result = await ps.GetPublisherExpectedReward(publisher.Id);
        return Ok(result);
    }

    public class PublisherFeatureRequest
    {
        [Required]
        public string Flag { get; set; } = null!;
        public Instant? ExpiredAt { get; set; }
    }

    [HttpPost("{name}/features")]
    [Authorize]
    [AskPermission("publishers.features")]
    public async Task<ActionResult<SnPublisherFeature>> AddPublisherFeature(
        string name,
        [FromBody] PublisherFeatureRequest request
    )
    {
        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

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

    [HttpDelete("{name}/features/{flag}")]
    [Authorize]
    [AskPermission("publishers.features")]
    public async Task<ActionResult> RemovePublisherFeature(string name, string flag)
    {
        var publisher = await db.Publishers.Where(p => p.Name == name).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

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
    public async Task<IActionResult> PerformLotteryDraw()
    {
        await ps.SettlePublisherRewards();
        return Ok();
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
            return StatusCode(403, "You need at least be manager to enable fediverse for this publisher.");

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
            return StatusCode(403, "You need at least be manager to disable fediverse for this publisher.");

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
