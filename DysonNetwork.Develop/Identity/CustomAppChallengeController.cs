using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/connect")]
public class CustomAppChallengeController(
    DeveloperService ds,
    DevProjectService projectService,
    CustomAppService customApps
) : ControllerBase
{
    public record ValidateAppConnectChallengeRequest(
        [MaxLength(8192)] string Challenge,
        [MaxLength(8192)] string Signature,
        string? SecretId = null
    );

    [HttpPost("{appId:guid}/validate")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateAppConnectChallenge(
        [FromRoute] Guid appId,
        [FromBody] ValidateAppConnectChallengeRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.Challenge) || string.IsNullOrWhiteSpace(request.Signature))
            return BadRequest("Challenge and signature are required.");

        var app = await customApps.GetAppAsync(appId);
        if (app is null)
            return NotFound("App not found");

        Guid? secretId = null;
        if (!string.IsNullOrWhiteSpace(request.SecretId))
        {
            if (!Guid.TryParse(request.SecretId, out var parsedSecretId))
                return BadRequest("Invalid secret id.");
            secretId = parsedSecretId;
        }

        var matchedSecret = await customApps.ValidateAppConnectChallengeSignatureAsync(
            appId,
            request.Challenge,
            request.Signature,
            secretId
        );

        return Ok(new CustomAppController.ValidateAppConnectChallengeResponse(
            matchedSecret is not null,
            matchedSecret?.Id.ToString()
        ));
    }
}