using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("api/bots")]
public class BotAccountPublicController(BotAccountService botService, DeveloperService developerService) : ControllerBase
{
    [HttpGet("{botId:guid}")]
    public async Task<ActionResult<SnBotAccount>> GetBotTransparentInfo([FromRoute] Guid botId)
    {
        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null) return NotFound(new ApiError { Code = "DEV_BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });
        bot = await botService.LoadBotAccountAsync(bot);

        var developer = await developerService.GetDeveloperById(bot!.Project.DeveloperId);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        bot.Developer = await developerService.LoadDeveloperPublisher(developer);

        return Ok(bot);
    }

    [HttpGet("{botId:guid}/developer")]
    public async Task<ActionResult<SnDeveloper>> GetBotDeveloper([FromRoute] Guid botId)
    {
        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null) return NotFound(new ApiError { Code = "DEV_BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        var developer = await developerService.GetDeveloperById(bot!.Project.DeveloperId);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_BOT_ACCOUNT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        developer = await developerService.LoadDeveloperPublisher(developer);

        return Ok(developer);
    }

    [HttpGet("{botId:guid}/chat")]
    public async Task<ActionResult<SnBotChatConfig>> GetBotChatConfig([FromRoute] Guid botId)
    {
        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null) return NotFound(new ApiError { Code = "DEV_BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        var config = await botService.GetChatConfigOrNullAsync(botId);
        if (config is null)
        {
            // Return default config
            return Ok(new SnBotChatConfig
            {
                Id = botId,
                AutoApproveDm = true,
                AutoApproveGroupChat = false,
                SupportChat = true,
                SubscribedEvents = ["messages.new"]
            });
        }

        return Ok(config);
    }

    [HttpGet("{botId:guid}/commands")]
    public async Task<ActionResult<List<SnBotCommand>>> GetBotCommands([FromRoute] Guid botId)
    {
        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null) return NotFound(new ApiError { Code = "DEV_BOT_ACCOUNT_NOT_FOUND", Message = "Bot not found", Status = 404 });

        var config = await botService.GetChatConfigOrNullAsync(botId);
        return Ok(config?.Commands ?? []);
    }
}