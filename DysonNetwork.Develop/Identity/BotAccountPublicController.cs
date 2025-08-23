using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("api/bots")]
public class BotAccountPublicController(BotAccountService botService, DeveloperService developerService) : ControllerBase
{
    [HttpGet("{botId:guid}")]
    public async Task<ActionResult<BotAccount>> GetBotTransparentInfo([FromRoute] Guid botId)
    {
        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null) return NotFound("Bot not found");
        bot = await botService.LoadBotAccountAsync(bot);

        var developer = await developerService.GetDeveloperById(bot!.Project.DeveloperId);
        if (developer is null) return NotFound("Developer not found");
        bot.Developer = await developerService.LoadDeveloperPublisher(developer);

        return Ok(bot);
    }

    [HttpGet("{botId:guid}/developer")]
    public async Task<ActionResult<Developer>> GetBotDeveloper([FromRoute] Guid botId)
    {
        var bot = await botService.GetBotByIdAsync(botId);
        if (bot is null) return NotFound("Bot not found");
        
        var developer = await developerService.GetDeveloperById(bot!.Project.DeveloperId);
        if (developer is null) return NotFound("Developer not found");
        developer = await developerService.LoadDeveloperPublisher(developer);

        return Ok(developer);
    }
}