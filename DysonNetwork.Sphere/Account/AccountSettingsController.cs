using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.ActivityPub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;

namespace DysonNetwork.Sphere.Account;

[ApiController]
[Route("api/account/publishing")]
[Authorize]
[ApiFeature("accounts.publishing-settings", Revision = 1)]
public class PublishingSettingsController(
    AppDatabase db,
    ActivityRenderer objFactory,
    ILogger<PublishingSettingsController> logger
) : ControllerBase
{
    public class PublishingSettingsRequest
    {
        public Guid? DefaultPostingPublisherId { get; set; }
        public Guid? DefaultReplyPublisherId { get; set; }
        public Guid? DefaultFediversePublisherId { get; set; }
    }

    [HttpGet]
    public async Task<ActionResult<SnPublishingSettings>> GetSettings()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var accountId = Guid.Parse(currentUser.Id);

        var settings = await db.PublishingSettings
            .FirstOrDefaultAsync(s => s.AccountId == accountId);

        if (settings == null)
        {
            settings = new SnPublishingSettings { AccountId = accountId };
            db.PublishingSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        return Ok(settings);
    }

    [HttpPatch]
    public async Task<ActionResult<SnPublishingSettings>> UpdateSettings([FromBody] PublishingSettingsRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var accountId = Guid.Parse(currentUser.Id);

        var settings = await db.PublishingSettings
            .FirstOrDefaultAsync(s => s.AccountId == accountId);

        if (settings == null)
        {
            settings = new SnPublishingSettings { AccountId = accountId };
            db.PublishingSettings.Add(settings);
        }

        if (request.DefaultPostingPublisherId != null)
        {
            var publisher = await db.Publishers
                .FirstOrDefaultAsync(p => p.Id == request.DefaultPostingPublisherId);
            if (publisher == null)
                return BadRequest(new ApiError { Code = "PUBLISHER_NOT_FOUND", Message = "Publisher not found", Status = 400 });
            if (!await IsMemberAsync(publisher.Id, accountId))
                return BadRequest(new ApiError { Code = "PUBLISHER_NOT_A_MEMBER", Message = "You are not a member of this publisher", Status = 400 });
            settings.DefaultPostingPublisherId = request.DefaultPostingPublisherId;
        }

        if (request.DefaultReplyPublisherId != null)
        {
            var publisher = await db.Publishers
                .FirstOrDefaultAsync(p => p.Id == request.DefaultReplyPublisherId);
            if (publisher == null)
                return BadRequest(new ApiError { Code = "PUBLISHER_NOT_FOUND", Message = "Publisher not found", Status = 400 });
            if (!await IsMemberAsync(publisher.Id, accountId))
                return BadRequest(new ApiError { Code = "PUBLISHER_NOT_A_MEMBER", Message = "You are not a member of this publisher", Status = 400 });
            settings.DefaultReplyPublisherId = request.DefaultReplyPublisherId;
        }

        if (request.DefaultFediversePublisherId != null)
        {
            var publisher = await db.Publishers
                .FirstOrDefaultAsync(p => p.Id == request.DefaultFediversePublisherId);
            if (publisher == null)
                return BadRequest(new ApiError { Code = "PUBLISHER_NOT_FOUND", Message = "Publisher not found", Status = 400 });
            if (!await IsMemberAsync(publisher.Id, accountId))
                return BadRequest(new ApiError { Code = "PUBLISHER_NOT_A_MEMBER", Message = "You are not a member of this publisher", Status = 400 });

            var actor = await objFactory.GetLocalActorAsync(request.DefaultFediversePublisherId.Value);
            if (actor == null)
                return BadRequest(new ApiError { Code = "PUBLISHER_FEDIVERSE_NOT_ENABLED", Message = "Fediverse is not enabled for this publisher", Status = 400 });
            settings.DefaultFediversePublisherId = request.DefaultFediversePublisherId;
        }

        settings.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        logger.LogInformation("Updated account settings for {AccountId}", accountId);

        return Ok(settings);
    }

    private async Task<bool> IsMemberAsync(Guid publisherId, Guid accountId)
    {
        return await db.PublisherMembers
            .AnyAsync(m => m.PublisherId == publisherId && m.AccountId == accountId && m.JoinedAt != null);
    }
}
