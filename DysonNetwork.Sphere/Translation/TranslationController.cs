using DysonNetwork.Pass.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Translation;

[ApiController]
[Route("translate")]
public class TranslationController(ITranslationProvider provider) : ControllerBase
{
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<string>> Translate([FromBody] string text, [FromQuery] string targetLanguage)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        if (currentUser.PerkSubscription is null)
            return StatusCode(403, "You need a subscription to use this feature.");

        return await provider.Translate(text, targetLanguage);
    }
}