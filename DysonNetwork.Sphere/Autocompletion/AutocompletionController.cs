using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Autocompletion;

[ApiController]
[Route("/api/autocomplete")]
[ApiFeature("autocomplete", Revision = 1)]
public class AutocompletionController(AutocompletionService aus) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<List<Shared.Models.Autocompletion>>> TextAutocomplete([FromBody] AutocompletionRequest request, Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var result = await aus.GetAutocompletion(request.Content, chatId: roomId, limit: 10);
        return Ok(result);
    }
}
