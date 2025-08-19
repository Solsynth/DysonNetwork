using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/developers/{pubName}/projects/{projectId:guid}/bots")]
[Authorize]
public class BotAccountController(
    BotAccountService botService,
    DeveloperService developerService,
    DevProjectService projectService,
    ILogger<BotAccountController> logger
)
    : ControllerBase
{
    public record BotRequest(
        [Required] [MaxLength(1024)] string? Slug
    );

    public record UpdateBotRequest(
       [MaxLength(1024)] string? Slug,
        bool? IsActive
    ) : BotRequest(Slug);

    [HttpGet]
    public async Task<IActionResult> ListBots(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await developerService.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await developerService.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to list bots");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var bots = await botService.GetBotsByProjectAsync(projectId);
        return Ok(bots);
    }

    [HttpGet("{botId:guid}")]
    public async Task<IActionResult> GetBot(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid botId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await developerService.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await developerService.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to view bot details");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null || bot.ProjectId != projectId)
            return NotFound("Bot not found");

        return Ok(bot);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBot(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromBody] BotRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest("Name is required");
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await developerService.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await developerService.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to create a bot");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        try
        {
            var bot = await botService.CreateBotAsync(project, request.Slug);
            return Ok(bot);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating bot account");
            return StatusCode(500, "An error occurred while creating the bot account");
        }
    }

    [HttpPut("{botId:guid}")]
    public async Task<IActionResult> UpdateBot(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid botId,
        [FromBody] UpdateBotRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await developerService.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await developerService.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to update a bot");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null || bot.ProjectId != projectId)
            return NotFound("Bot not found");

        try
        {
            var updatedBot = await botService.UpdateBotAsync(
                bot,
                request.Slug,
                request.IsActive
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await developerService.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await developerService.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id),
                PublisherMemberRole.Editor))
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
}