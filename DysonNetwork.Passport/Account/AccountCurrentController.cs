using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using System.Text.Json;

namespace DysonNetwork.Passport.Account;

[Authorize]
[ApiController]
[Route("/api/accounts/me")]
public class AccountCurrentController(
    AppDatabase db,
    AccountService accounts,
    AccountBoardService boards,
    ApplePassService applePasses,
    RemoteAccountContactService remoteContacts,
    RemoteAccountConnectionService remoteConnections,
    DyFileService.DyFileServiceClient files,
    Credit.SocialCreditService creditService,
    RemoteSubscriptionService remoteSubscription,
    RemoteActionLogService remoteActionLogs
) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SnAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SnAccount>> GetCurrentIdentity()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var account = await accounts.GetAccount(userId);

        if (account != null)
        {
            if (account.Profile is null)
            {
                account.Profile = await accounts.GetOrCreateAccountProfileAsync(account.Id);
            }

            // Populate PerkSubscription from Wallet service via gRPC
            try
            {
                var subscription = await remoteSubscription.GetPerkSubscription(account.Id);
                if (subscription is not null)
                {
                    account.PerkSubscription = SnWalletSubscription.FromProtoValue(subscription).ToReference();
                    account.PerkLevel = account.PerkSubscription.PerkLevel;
                }
                else
                {
                    account.PerkSubscription = null;
                    account.PerkLevel = 0;
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail the request - PerkSubscription is optional
                Console.WriteLine($"Failed to populate PerkSubscription for account {account.Id}: {ex.Message}");
            }

            account.Contacts = await remoteContacts.ListContactsAsync(account.Id);
        }

        return Ok(account);
    }

    [HttpGet("passbook/member")]
    [Produces("application/vnd.apple.pkpass")]
    public async Task<ActionResult> GetMemberPass(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var bytes = await applePasses.GenerateMemberPassAsync(currentUser.Id, cancellationToken);
        return File(bytes, "application/vnd.apple.pkpass", "solian-member.pkpass");
    }


    public class ProfileRequest
    {
        [MaxLength(256)] public string? FirstName { get; set; }
        [MaxLength(256)] public string? MiddleName { get; set; }
        [MaxLength(256)] public string? LastName { get; set; }
        [MaxLength(1024)] public string? Gender { get; set; }
        [MaxLength(1024)] public string? Pronouns { get; set; }
        [MaxLength(1024)] public string? TimeZone { get; set; }
        [MaxLength(1024)] public string? Location { get; set; }
        [MaxLength(4096)] public string? Bio { get; set; }
        public Shared.Models.UsernameColor? UsernameColor { get; set; }
        public Instant? Birthday { get; set; }
        public List<SnProfileLink>? Links { get; set; }

        [MaxLength(32)] public string? PictureId { get; set; }
        [MaxLength(32)] public string? BackgroundId { get; set; }
    }

    public class BoardItemRequest
    {
        public Guid? Id { get; set; }
        public int Order { get; set; }
        public SnAccountBoardItemKind Kind { get; set; }
        [MaxLength(256)] public string? WidgetKey { get; set; }
        public Guid? CustomAppId { get; set; }
        [MaxLength(256)] public string? CustomAppWidgetKey { get; set; }
        public bool IsEnabled { get; set; } = true;
        public Dictionary<string, object?>? Payload { get; set; }

        public SnAccountBoardItem ToModel()
        {
            return new SnAccountBoardItem
            {
                Id = Id ?? Guid.NewGuid(),
                Order = Order,
                Kind = Kind,
                WidgetKey = WidgetKey,
                CustomAppId = CustomAppId,
                CustomAppWidgetKey = CustomAppWidgetKey,
                IsEnabled = IsEnabled,
                Payload = Payload ?? []
            };
        }
    }

    [HttpPatch("profile")]
    public async Task<ActionResult<SnAccountProfile>> UpdateProfile([FromBody] ProfileRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var profile = await accounts.GetOrCreateAccountProfileAsync(userId);
        var changedFields = new List<string>();

        var wasProfileComplete = IsProfileComplete(profile);

        if (request.FirstName is not null) { profile.FirstName = request.FirstName; changedFields.Add("first_name"); }
        if (request.MiddleName is not null) { profile.MiddleName = request.MiddleName; changedFields.Add("middle_name"); }
        if (request.LastName is not null) { profile.LastName = request.LastName; changedFields.Add("last_name"); }
        if (request.Bio is not null) { profile.Bio = request.Bio; changedFields.Add("bio"); }
        if (request.Gender is not null) { profile.Gender = request.Gender; changedFields.Add("gender"); }
        if (request.Pronouns is not null) { profile.Pronouns = request.Pronouns; changedFields.Add("pronouns"); }
        if (request.Birthday is not null) { profile.Birthday = request.Birthday; changedFields.Add("birthday"); }
        if (request.Location is not null) { profile.Location = request.Location; changedFields.Add("location"); }
        if (request.TimeZone is not null) { profile.TimeZone = request.TimeZone; changedFields.Add("time_zone"); }
        if (request.Links is not null) { profile.Links = request.Links; changedFields.Add("links"); }
        if (request.UsernameColor is not null) { profile.UsernameColor = request.UsernameColor; changedFields.Add("username_color"); }

        var hadPicture = profile.Picture is not null;
        if (request.PictureId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
            profile.Picture = SnCloudFileReferenceObject.FromProtoValue(file);
            changedFields.Add("picture");
        }

        if (request.BackgroundId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            profile.Background = SnCloudFileReferenceObject.FromProtoValue(file);
            changedFields.Add("background");
        }

        db.Update(profile);
        await db.SaveChangesAsync();
        if (changedFields.Count > 0)
        {
            remoteActionLogs.CreateActionLog(
                userId,
                ActionLogType.AccountProfileUpdate,
                new Dictionary<string, object> { ["fields"] = changedFields },
                Request.Headers.UserAgent.ToString(),
                Request.GetClientIpAddress()
            );
        }

        if (!hadPicture && profile.Picture is not null)
        {
            remoteActionLogs.CreateActionLog(
                userId,
                ActionLogType.AccountAvatar,
                new Dictionary<string, object>(),
                Request.Headers.UserAgent.ToString(),
                Request.GetClientIpAddress()
            );
        }

        if (!wasProfileComplete && IsProfileComplete(profile))
        {
            remoteActionLogs.CreateActionLog(
                userId,
                ActionLogType.AccountProfileComplete,
                new Dictionary<string, object>(),
                Request.Headers.UserAgent.ToString(),
                Request.GetClientIpAddress()
            );
        }

        await accounts.PurgeAccountCache(currentUser);

        return profile;
    }

    [HttpGet("board")]
    [AskPermission(PermissionKeys.AccountsProfileBoardManage)]
    public async Task<ActionResult<List<SnAccountBoardItem>>> GetBoard()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        return Ok(await boards.GetBoardAsync(currentUser.Id));
    }

    [HttpPut("board")]
    [AskPermission(PermissionKeys.AccountsProfileBoardManage)]
    public async Task<ActionResult<List<SnAccountBoardItem>>> ReplaceBoard([FromBody] List<BoardItemRequest> request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var board = await boards.ReplaceBoardAsync(currentUser.Id, request.Select(x => x.ToModel()));
            await accounts.PurgeAccountCache(currentUser);
            return Ok(board);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static bool IsProfileComplete(SnAccountProfile p)
    {
        return !string.IsNullOrWhiteSpace(p.FirstName)
            && !string.IsNullOrWhiteSpace(p.LastName)
            && !string.IsNullOrWhiteSpace(p.Bio)
            && !string.IsNullOrWhiteSpace(p.Location)
            && !string.IsNullOrWhiteSpace(p.Pronouns)
            && p.Birthday is not null
            && p.Picture is not null;
    }

    [HttpDelete]
    public async Task<ActionResult> RequestDeleteAccount()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            await accounts.RequestAccountDeletion(currentUser);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new ApiError
            {
                Code = "TOO_MANY_REQUESTS",
                Message = "You already requested account deletion within 24 hours.",
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });
        }

        return Ok();
    }

    [HttpGet("actions")]
    [ProducesResponseType<List<SnActionLog>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<SnActionLog>>> GetActionLogs(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var page = await remoteActionLogs.ListActionLogsPage(
            currentUser.Id,
            pageSize: Math.Max(1, take),
            pageToken: Math.Max(0, offset).ToString(),
            orderBy: "createdat desc");

        Response.Headers.Append("X-Total", page.TotalSize.ToString());

        var logs = page.ActionLogs.Select(log =>
        {
            var meta = log.Meta
                .Select(x => new KeyValuePair<string, object?>(x.Key, InfraObjectCoder.ConvertValueToObject(x.Value)))
                .Where(x => x.Value is not null)
                .ToDictionary(x => x.Key, x => x.Value!);

            Guid? sessionId = null;
            if (!string.IsNullOrWhiteSpace(log.SessionId) && Guid.TryParse(log.SessionId, out var parsedSessionId))
                sessionId = parsedSessionId;

            GeoPoint? location = null;
            if (!string.IsNullOrWhiteSpace(log.Location))
            {
                try
                {
                    location = JsonSerializer.Deserialize<GeoPoint>(log.Location);
                }
                catch (JsonException)
                {
                }
            }

            return new SnActionLog
            {
                Id = Guid.TryParse(log.Id, out var parsedId) ? parsedId : Guid.NewGuid(),
                AccountId = currentUser.Id,
                Action = log.Action,
                Meta = meta,
                UserAgent = string.IsNullOrWhiteSpace(log.UserAgent) ? null : log.UserAgent,
                IpAddress = string.IsNullOrWhiteSpace(log.IpAddress) ? null : log.IpAddress,
                Location = location,
                SessionId = sessionId,
                CreatedAt = log.CreatedAt.ToInstant(),
                UpdatedAt = log.CreatedAt.ToInstant()
            };
        }).ToList();

        return Ok(logs);
    }

    [HttpGet("badges")]
    [ProducesResponseType<List<SnAccountBadge>>(StatusCodes.Status200OK)]
    [Authorize]
    public async Task<ActionResult<List<SnAccountBadge>>> GetBadges()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var badges = await db.Badges
            .Where(b => b.AccountId == currentUser.Id)
            .ToListAsync();
        return Ok(badges);
    }

    [HttpPost("badges/{id:guid}/active")]
    [Authorize]
    [AskPermission(PermissionKeys.ProgressionBadgesManage)]
    public async Task<ActionResult<SnAccountBadge>> ActivateBadge(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            await accounts.ActiveBadge(currentUser, id);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("leveling")]
    [Authorize]
    public async Task<ActionResult<SnExperienceRecord>> GetLevelingHistory(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var queryable = db.ExperienceRecords
            .Where(r => r.AccountId == currentUser.Id)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        var totalCount = await queryable.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var records = await queryable
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        return Ok(records);
    }

    [HttpGet("credits")]
    public async Task<ActionResult<bool>> GetSocialCredit()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var credit = await creditService.GetSocialCredit(currentUser.Id);
        return Ok(credit);
    }

    [HttpGet("credits/history")]
    public async Task<ActionResult<SnSocialCreditRecord>> GetCreditHistory(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var queryable = db.SocialCreditRecords
            .Where(r => r.AccountId == currentUser.Id)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        var totalCount = await queryable.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var records = await queryable
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        return Ok(records);
    }

    [HttpGet("connections")]
    [AskPermission("account.connections")]
    [ProducesResponseType<List<SnAccountConnection>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SnAccountConnection>>> GetConnections(
        [FromQuery] string? provider = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var connections = await remoteConnections.ListConnectionsAsync(currentUser.Id, provider);
        return Ok(connections);
    }
}
