using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Autocompletion;

[ApiController]
[Route("/api/autocomplete")]
public class AutocompletionController(AutocompletionService aus) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<List<DysonNetwork.Shared.Models.Autocompletion>>> TextAutocomplete([FromBody] AutocompletionRequest request, Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var result = await aus.GetAutocompletion(request.Content, chatId: roomId, limit: 10);
        return Ok(result);
    }
}
