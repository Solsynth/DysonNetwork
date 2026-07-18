using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/private/bots")]
[Authorize]
[ApiFeature("developers.bots", Revision = 1)]
[ApiFeature("developers.bots.keys", Revision = 1)]
[ApiFeature("developers.bots.chat", Revision = 1)]
public class BotAccountController(
    BotAccountService botService,
    DeveloperQuotaService quotaService,
    DeveloperService ds,
    DevProjectService projectService,
    ILogger<BotAccountController> logger,
    RemoteAccountService remoteAccounts,
    DyBotAccountReceiverService.DyBotAccountReceiverServiceClient accountsReceiver,
    IEventBus eventBus
)
    : ControllerBase
{
    public class CommonBotRequest
    {
        [MaxLength(256)] public string? FirstName { get; set; }
        [MaxLength(256)] public string? MiddleName { get; set; }
        [MaxLength(256)] public string? LastName { get; set; }
        [MaxLength(1024)] public string? Gender { get; set; }
        [MaxLength(1024)] public string? Pronouns { get; set; }
        [MaxLength(1024)] public string? TimeZone { get; set; }
        [MaxLength(1024)] public string? Location { get; set; }
        [MaxLength(4096)] public string? Bio { get; set; }
        public Instant? Birthday { get; set; }

        [MaxLength(32)] public string? PictureId { get; set; }
        [MaxLength(32)] public string? BackgroundId { get; set; }
    }

    public class BotCreateRequest : CommonBotRequest
    {
        [Required]
        [MinLength(2)]
        [MaxLength(256)]
        [RegularExpression(@"^[A-Za-z0-9_-]+$",
            ErrorMessage = "Name can only contain letters, numbers, underscores, and hyphens.")
        ]
        public string Name { get; set; } = string.Empty;

        [Required] [MaxLength(256)] public string Nick { get; set; } = string.Empty;

        [Required] [MaxLength(1024)] public string Slug { get; set; } = string.Empty;

        [MaxLength(128)] public string Language { get; set; } = "en-us";
    }

    public class UpdateBotRequest : CommonBotRequest
    {
        [MinLength(2)]
        [MaxLength(256)]
        [RegularExpression(@"^[A-Za-z0-9_-]+$",
            ErrorMessage = "Name can only contain letters, numbers, underscores, and hyphens.")
        ]
        public string? Name { get; set; } = string.Empty;

        [MaxLength(256)] public string? Nick { get; set; } = string.Empty;

        [Required] [MaxLength(1024)] public string? Slug { get; set; } = string.Empty;

        [MaxLength(128)] public string? Language { get; set; }

        public bool? IsActive { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> ListBots(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                DyPublisherMemberRole.DyViewer))
            return StatusCode(403, ApiError.Unauthorized("You must be an viewer of the developer to list bots", forbidden: true));

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var bots = await botService.GetBotsByProjectAsync(projectId);
        return Ok(await botService.LoadBotsAccountAsync(bots));
    }

    [HttpGet("{botId:guid}")]
    public async Task<IActionResult> GetBot(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                DyPublisherMemberRole.DyViewer))
            return StatusCode(403, ApiError.Unauthorized("You must be an viewer of the developer to view bot details", forbidden: true));

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null || bot.ProjectId != projectId)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        return Ok(await botService.LoadBotAccountAsync(bot));
    }

    [HttpPost]
    [AskPermission(PermissionKeys.BotAccountsCreate)]
    public async Task<IActionResult> CreateBot(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromBody] BotCreateRequest createRequest
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to create a bot", forbidden: true));

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var hydratedAccount = SnAccount.FromProtoValue(
            await remoteAccounts.GetAccount(Guid.Parse(currentUser.Id))
        );
        var quota = await quotaService.GetQuotaAsync(hydratedAccount);
        if (quota.Used >= quota.Total)
            return StatusCode(403, ApiError.Unauthorized($"Bot quota exceeded ({quota.Used}/{quota.Total}).", forbidden: true));

        var now = SystemClock.Instance.GetCurrentInstant();
        var accountId = Guid.NewGuid();
        var account = new DyAccount
        {
            Id = accountId.ToString(),
            Name = createRequest.Name,
            Nick = createRequest.Nick,
            Language = createRequest.Language,
            Profile = new DyAccountProfile
            {
                Id = Guid.NewGuid().ToString(),
                Bio = createRequest.Bio,
                Gender = createRequest.Gender,
                FirstName = createRequest.FirstName,
                MiddleName = createRequest.MiddleName,
                LastName = createRequest.LastName,
                TimeZone = createRequest.TimeZone,
                Pronouns = createRequest.Pronouns,
                Location = createRequest.Location,
                Birthday = createRequest.Birthday?.ToTimestamp(),
                AccountId = accountId.ToString(),
                CreatedAt = now.ToTimestamp(),
                UpdatedAt = now.ToTimestamp()
            },
            CreatedAt = now.ToTimestamp(),
            UpdatedAt = now.ToTimestamp()
        };

        try
        {
            var bot = await botService.CreateBotAsync(
                project,
                createRequest.Slug,
                account,
                createRequest.PictureId,
                createRequest.BackgroundId
            );
            return Ok(bot);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating bot account");
            return StatusCode(500, new ApiError { Code = "BOT_ACCOUNT_CREATE_ERROR", Message = "An error occurred while creating the bot account", Status = 500 });
        }
    }

    [HttpPatch("{botId:guid}")]
    [AskPermission(PermissionKeys.BotAccountsUpdate)]
    public async Task<IActionResult> UpdateBot(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId,
        [FromBody] UpdateBotRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to update a bot", forbidden: true));

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null || bot.ProjectId != projectId)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        var botAccount = await remoteAccounts.GetBotAccount(bot.Id);

        if (request.Name is not null) botAccount.Name = request.Name;
        if (request.Nick is not null) botAccount.Nick = request.Nick;
        if (request.Language is not null) botAccount.Language = request.Language;
        if (request.Bio is not null) botAccount.Profile.Bio = request.Bio;
        if (request.Gender is not null) botAccount.Profile.Gender = request.Gender;
        if (request.FirstName is not null) botAccount.Profile.FirstName = request.FirstName;
        if (request.MiddleName is not null) botAccount.Profile.MiddleName = request.MiddleName;
        if (request.LastName is not null) botAccount.Profile.LastName = request.LastName;
        if (request.TimeZone is not null) botAccount.Profile.TimeZone = request.TimeZone;
        if (request.Pronouns is not null) botAccount.Profile.Pronouns = request.Pronouns;
        if (request.Location is not null) botAccount.Profile.Location = request.Location;
        if (request.Birthday is not null) botAccount.Profile.Birthday = request.Birthday?.ToTimestamp();

        if (request.Slug is not null) bot.Slug = request.Slug;
        if (request.IsActive is not null) bot.IsActive = request.IsActive.Value;

        try
        {
            var updatedBot = await botService.UpdateBotAsync(
                bot,
                botAccount,
                request.PictureId,
                request.BackgroundId
            );

            return Ok(updatedBot);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating bot account {BotId}", botId);
            return StatusCode(500, new ApiError { Code = "BOT_ACCOUNT_UPDATE_ERROR", Message = "An error occurred while updating the bot account", Status = 500 });
        }
    }

    [HttpDelete("{botId:guid}")]
    [AskPermission(PermissionKeys.BotAccountsDelete)]
    public async Task<IActionResult> DeleteBot(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to delete a bot", forbidden: true));

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null || bot.ProjectId != projectId)
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        try
        {
            await botService.DeleteBotAsync(bot);
            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting bot {BotId}", botId);
            return StatusCode(500, new ApiError { Code = "BOT_ACCOUNT_DELETE_ERROR", Message = "An error occurred while deleting the bot account", Status = 500 });
        }
    }

    [HttpGet("{botId:guid}/keys")]
    public async Task<ActionResult<List<SnApiKey>>> ListBotKeys(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyViewer);
        if (developer == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        if (project == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });
        if (bot == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        var keys = await accountsReceiver.ListApiKeyAsync(new DyListApiKeyRequest
        {
            AutomatedId = bot.Id.ToString()
        });
        var data = keys.Data.Select(SnApiKey.FromProtoValue).ToList();

        return Ok(data);
    }

    [HttpGet("{botId:guid}/keys/{keyId:guid}")]
    public async Task<ActionResult<SnApiKey>> GetBotKey(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId,
        [FromRoute] Guid keyId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyViewer);
        if (developer == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        if (project == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });
        if (bot == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        try
        {
            var key = await accountsReceiver.GetApiKeyAsync(new DyGetApiKeyRequest { Id = keyId.ToString() });
            if (key == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_API_KEY_NOT_FOUND", Message = "API key not found", Status = 404 });
            return Ok(SnApiKey.FromProtoValue(key));
        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_API_KEY_NOT_FOUND", Message = "API key not found", Status = 404 });
        }
    }

    public class CreateApiKeyRequest
    {
        [Required, MaxLength(1024)] public string Label { get; set; } = null!;
    }

    [HttpPost("{botId:guid}/keys")]
    [AskPermission(PermissionKeys.BotAccountsKeysManage)]
    public async Task<ActionResult<SnApiKey>> CreateBotKey(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId,
        [FromBody] CreateApiKeyRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyEditor);
        if (developer == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        if (project == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });
        if (bot == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        try
        {
            var newKey = new DyApiKey
            {
                AccountId = bot.Id.ToString(),
                Label = request.Label
            };

            var createdKey = await accountsReceiver.CreateApiKeyAsync(newKey);
            return Ok(SnApiKey.FromProtoValue(createdKey));
        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
        {
            return BadRequest(new ApiError { Code = "DEV_BOT_ACCOUNT_BAD_REQUEST", Message = ex.Status.Detail, Status = 400 });
        }
    }

    [HttpPost("{botId:guid}/keys/{keyId:guid}/rotate")]
    [AskPermission(PermissionKeys.BotAccountsKeysManage)]
    public async Task<ActionResult<SnApiKey>> RotateBotKey(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId,
        [FromRoute] Guid keyId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyEditor);
        if (developer == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        if (project == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });
        if (bot == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        try
        {
            var rotatedKey = await accountsReceiver.RotateApiKeyAsync(new DyGetApiKeyRequest { Id = keyId.ToString() });
            return Ok(SnApiKey.FromProtoValue(rotatedKey));
        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_API_KEY_NOT_FOUND", Message = "API key not found", Status = 404 });
        }
    }

    [HttpDelete("{botId:guid}/keys/{keyId:guid}")]
    [AskPermission(PermissionKeys.BotAccountsKeysManage)]
    public async Task<IActionResult> DeleteBotKey(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId,
        [FromRoute] Guid keyId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyEditor);
        if (developer == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        if (project == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });
        if (bot == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        try
        {
            await accountsReceiver.DeleteApiKeyAsync(new DyGetApiKeyRequest { Id = keyId.ToString() });
            return NoContent();
        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return NotFound(new ApiError { Code = "BOT_ACCOUNT_API_KEY_NOT_FOUND", Message = "API key not found", Status = 404 });
        }
    }

    // --- Bot Chat Config Endpoints ---

    [HttpGet("{botId:guid}/chat")]
    public async Task<ActionResult<SnBotChatConfig>> GetChatConfig(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyViewer);
        if (developer == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        if (project == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });
        if (bot == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        var config = await botService.GetChatConfigAsync(botId);
        return Ok(config);
    }

    [HttpPut("{botId:guid}/chat")]
    [AskPermission(PermissionKeys.BotAccountsChatManage)]
    public async Task<ActionResult<SnBotChatConfig>> UpdateChatConfig(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId,
        [FromBody] SnBotChatConfig request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyEditor);
        if (developer == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        if (project == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });
        if (bot == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        var config = await botService.UpdateChatConfigAsync(botId, request);

        // Publish event to notify other services about the config change
        await eventBus.PublishAsync(new BotChatConfigUpdatedEvent
        {
            BotAccountId = botId,
            UpdatedAt = SystemClock.Instance.GetCurrentInstant()
        });

        return Ok(config);
    }

    public class BotManifestRequest
    {
        public List<SnBotCommand> Commands { get; set; } = [];
        public bool? AutoApproveDm { get; set; }
        public bool? AutoApproveGroupChat { get; set; }
        public bool? SupportChat { get; set; }
        public List<string>? SubscribedEvents { get; set; }
    }

    [HttpPost("{botId:guid}/chat/manifest")]
    [AskPermission(PermissionKeys.BotAccountsChatManage)]
    public async Task<ActionResult<SnBotChatConfig>> UpdateManifest(
        [FromQuery(Name = "dev")] string pubName,
        [FromQuery(Name = "proj")] Guid projectId,
        [FromRoute] Guid botId,
        [FromBody] BotManifestRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyEditor);
        if (developer == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        if (project == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });
        if (bot == null) return NotFound(new ApiError { Code = "BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        // Get existing config and merge
        var existingConfig = await botService.GetChatConfigOrNullAsync(botId)
            ?? new SnBotChatConfig { Id = botId };

        existingConfig.Commands = request.Commands;
        if (request.AutoApproveDm.HasValue) existingConfig.AutoApproveDm = request.AutoApproveDm.Value;
        if (request.AutoApproveGroupChat.HasValue) existingConfig.AutoApproveGroupChat = request.AutoApproveGroupChat.Value;
        if (request.SupportChat.HasValue) existingConfig.SupportChat = request.SupportChat.Value;
        if (request.SubscribedEvents is not null) existingConfig.SubscribedEvents = request.SubscribedEvents;

        var config = await botService.UpdateChatConfigAsync(botId, existingConfig);

        // Publish event to notify other services about the config change
        await eventBus.PublishAsync(new BotChatConfigUpdatedEvent
        {
            BotAccountId = botId,
            UpdatedAt = SystemClock.Instance.GetCurrentInstant()
        });

        return Ok(config);
    }

    private async Task<(SnDeveloper?, SnDevProject?, SnBotAccount?)> ValidateBotAccess(
        string pubName,
        Guid projectId,
        Guid botId,
        DyAccount currentUser,
        DyPublisherMemberRole requiredRole
    )
    {
        var developer = await ds.GetDeveloperByName(pubName);
        if (developer == null) return (null, null, null);

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), requiredRole))
            return (null, null, null);

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project == null) return (developer, null, null);

        var bot = await botService.GetBotByIdAsync(botId);
        if (bot == null || bot.ProjectId != projectId) return (developer, project, null);

        return (developer, project, bot);
    }
}
