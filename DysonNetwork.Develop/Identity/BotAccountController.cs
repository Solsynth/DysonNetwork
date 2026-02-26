using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/developers/{pubName}/projects/{projectId:guid}/bots")]
[Authorize]
public class BotAccountController(
    BotAccountService botService,
    DeveloperService ds,
    DevProjectService projectService,
    ILogger<BotAccountController> logger,
    RemoteAccountService remoteAccounts,
    DyBotAccountReceiverService.DyBotAccountReceiverServiceClient accountsReceiver
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
        [FromRoute] string pubName,
        [FromRoute] Guid projectId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                DyPublisherMemberRole.DyViewer))
            return StatusCode(403, "You must be an viewer of the developer to list bots");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var bots = await botService.GetBotsByProjectAsync(projectId);
        return Ok(await botService.LoadBotsAccountAsync(bots));
    }

    [HttpGet("{botId:guid}")]
    public async Task<IActionResult> GetBot(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid botId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                DyPublisherMemberRole.DyViewer))
            return StatusCode(403, "You must be an viewer of the developer to view bot details");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null || bot.ProjectId != projectId)
            return NotFound("Bot not found");

        return Ok(await botService.LoadBotAccountAsync(bot));
    }

    [HttpPost]
    public async Task<IActionResult> CreateBot(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromBody] BotCreateRequest createRequest
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to create a bot");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

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
            return StatusCode(500, "An error occurred while creating the bot account");
        }
    }

    [HttpPatch("{botId:guid}")]
    public async Task<IActionResult> UpdateBot(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid botId,
        [FromBody] UpdateBotRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to update a bot");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null || bot.ProjectId != projectId)
            return NotFound("Bot not found");

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
            return StatusCode(500, "An error occurred while updating the bot account");
        }
    }

    [HttpDelete("{botId:guid}")]
    public async Task<IActionResult> DeleteBot(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid botId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to delete a bot");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null || bot.ProjectId != projectId)
            return NotFound("Bot not found");

        try
        {
            await botService.DeleteBotAsync(bot);
            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting bot {BotId}", botId);
            return StatusCode(500, "An error occurred while deleting the bot account");
        }
    }

    [HttpGet("{botId:guid}/keys")]
    public async Task<ActionResult<List<SnApiKey>>> ListBotKeys(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid botId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyViewer);
        if (developer == null) return NotFound("Developer not found");
        if (project == null) return NotFound("Project not found or you don't have access");
        if (bot == null) return NotFound("Bot not found");

        var keys = await accountsReceiver.ListApiKeyAsync(new DyListApiKeyRequest
        {
            AutomatedId = bot.Id.ToString()
        });
        var data = keys.Data.Select(SnApiKey.FromProtoValue).ToList();

        return Ok(data);
    }

    [HttpGet("{botId:guid}/keys/{keyId:guid}")]
    public async Task<ActionResult<SnApiKey>> GetBotKey(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid botId,
        [FromRoute] Guid keyId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyViewer);
        if (developer == null) return NotFound("Developer not found");
        if (project == null) return NotFound("Project not found or you don't have access");
        if (bot == null) return NotFound("Bot not found");

        try
        {
            var key = await accountsReceiver.GetApiKeyAsync(new DyGetApiKeyRequest { Id = keyId.ToString() });
            if (key == null) return NotFound("API key not found");
            return Ok(SnApiKey.FromProtoValue(key));
        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return NotFound("API key not found");
        }
    }

    public class CreateApiKeyRequest
    {
        [Required, MaxLength(1024)] public string Label { get; set; } = null!;
    }

    [HttpPost("{botId:guid}/keys")]
    public async Task<ActionResult<SnApiKey>> CreateBotKey(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid botId,
        [FromBody] CreateApiKeyRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyEditor);
        if (developer == null) return NotFound("Developer not found");
        if (project == null) return NotFound("Project not found or you don't have access");
        if (bot == null) return NotFound("Bot not found");

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
            return BadRequest(ex.Status.Detail);
        }
    }

    [HttpPost("{botId:guid}/keys/{keyId:guid}/rotate")]
    public async Task<ActionResult<SnApiKey>> RotateBotKey(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid botId,
        [FromRoute] Guid keyId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyEditor);
        if (developer == null) return NotFound("Developer not found");
        if (project == null) return NotFound("Project not found or you don't have access");
        if (bot == null) return NotFound("Bot not found");

        try
        {
            var rotatedKey = await accountsReceiver.RotateApiKeyAsync(new DyGetApiKeyRequest { Id = keyId.ToString() });
            return Ok(SnApiKey.FromProtoValue(rotatedKey));
        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return NotFound("API key not found");
        }
    }

    [HttpDelete("{botId:guid}/keys/{keyId:guid}")]
    public async Task<IActionResult> DeleteBotKey(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid botId,
        [FromRoute] Guid keyId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var (developer, project, bot) =
            await ValidateBotAccess(pubName, projectId, botId, currentUser, DyPublisherMemberRole.DyEditor);
        if (developer == null) return NotFound("Developer not found");
        if (project == null) return NotFound("Project not found or you don't have access");
        if (bot == null) return NotFound("Bot not found");

        try
        {
            await accountsReceiver.DeleteApiKeyAsync(new DyGetApiKeyRequest { Id = keyId.ToString() });
            return NoContent();
        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return NotFound("API key not found");
        }
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