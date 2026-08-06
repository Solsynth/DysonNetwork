using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Passport.Examination;
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
[ApiFeature("accounts.board", Revision = 1)]
[ApiFeature("accounts.badges", Revision = 1)]
[ApiFeature("accounts.leveling", Revision = 1)]
[ApiFeature("accounts.credits", Revision = 1)]
[ApiFeature("accounts.connections", Revision = 1)]
public class AccountCurrentController(
    AppDatabase db,
    AccountService accounts,
    AccountBoardService boards,
    ApplePassService applePasses,
    RemoteAccountConnectionService remoteConnections,
    Credit.SocialCreditService creditService,
    RemoteActionLogService remoteActionLogs,
    TestService tests
) : ControllerBase
{
    [HttpGet("activation/progress")]
    [AskPermission(PermissionKeys.TestsTake)]
    public async Task<ActionResult<ActivationRequirementState>> GetActivationProgress()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        return Ok(await tests.GetActivationRequirements(currentUser.Id, HttpContext.RequestAborted));
    }

    [HttpGet("passbook/member")]
    [Produces("application/vnd.apple.pkpass")]
    public async Task<ActionResult> GetMemberPass(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var bytes = await applePasses.GenerateMemberPassAsync(currentUser.Id, cancellationToken);
        return File(bytes, "application/vnd.apple.pkpass", "solian-member.pkpass");
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

    [HttpGet("board")]
    [AskPermission(PermissionKeys.AccountsProfileBoardManage)]
    public async Task<ActionResult<List<SnAccountBoardItem>>> GetBoard()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        return Ok(await boards.GetBoardAsync(currentUser.Id));
    }

    [HttpPut("board")]
    [AskPermission(PermissionKeys.AccountsProfileBoardManage)]
    public async Task<ActionResult<List<SnAccountBoardItem>>> ReplaceBoard([FromBody] List<BoardItemRequest> request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var board = await boards.ReplaceBoardAsync(currentUser.Id, request.Select(x => x.ToModel()));
            await accounts.PurgeAccountCache(currentUser);
            return Ok(board);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_BOARD_REPLACE_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpGet("actions")]
    [ProducesResponseType<List<SnActionLog>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<SnActionLog>>> GetActionLogs(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            await accounts.ActiveBadge(currentUser, id);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_BADGE_ACTIVATE_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpGet("leveling")]
    [Authorize]
    public async Task<ActionResult<SnExperienceRecord>> GetLevelingHistory(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var credit = await creditService.GetSocialCredit(currentUser.Id);
        return Ok(credit);
    }

    [HttpGet("credits/history")]
    public async Task<ActionResult<SnSocialCreditRecord>> GetCreditHistory(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var connections = await remoteConnections.ListConnectionsAsync(currentUser.Id, provider);
        return Ok(connections);
    }
}
