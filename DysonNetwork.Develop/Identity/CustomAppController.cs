using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Auth;
using Google.Protobuf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using System.Text.Json;
using Struct = Google.Protobuf.WellKnownTypes.Struct;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/private/apps")]
public class CustomAppController(
    CustomAppService customApps,
    DeveloperService ds,
    DevProjectService projectService,
    DyProfileService.DyProfileServiceClient profiles)
    : ControllerBase
{
    public record CustomAppRequest(
        [MaxLength(1024)] string? Slug,
        [MaxLength(1024)] string? Name,
        [MaxLength(4096)] string? Description,
        string? PictureId,
        string? BackgroundId,
        Shared.Models.CustomAppStatus? Status,
        SnCustomAppLinks? Links,
        SnCustomAppOauthConfig? OauthConfig,
        List<SnBoardWidgetManifest>? BoardWidgets
    );

    public record CreateSecretRequest(
        [MaxLength(4096)] string? Description,
        TimeSpan? ExpiresIn = null,
        CustomAppSecretType Type = CustomAppSecretType.ApiKey
    );

    public record SecretResponse(
        string Id,
        string? Secret,
        string? Description,
        Instant? ExpiresAt,
        CustomAppSecretType Type,
        Instant CreatedAt,
        Instant UpdatedAt
    );

    public record UpdateBoardPayloadRequest(
        string AccountId,
        string BoardItemId,
        [MaxLength(128)] string WidgetKey,
        Dictionary<string, object?>? Payload
    );



    [HttpGet]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsCreate)]
    public async Task<IActionResult> ListApps([FromQuery(Name = "dev")] string dev, [FromQuery(Name = "proj")] Guid proj)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyViewer))
            return StatusCode(403, "You must be a viewer of the developer to list custom apps");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound();

        var apps = await customApps.GetAppsByProjectAsync(proj);
        return Ok(apps);
    }

    [HttpGet("{appId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetApp(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyViewer))
            return StatusCode(403, "You must be a viewer of the developer to list custom apps");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound();

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound();

        return Ok(app);
    }

    [HttpPost("{appId:guid}/board")]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsUpdate)]
    public async Task<IActionResult> CreateBoardWidget(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromBody] SnBoardWidgetManifest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to manage board widgets");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound();

        var app = await customApps.GetAppAsync(appId, proj);
        if (app is null)
            return NotFound();

        try
        {
            var created = await customApps.CreateBoardWidgetAsync(appId, request);
            if (created is null)
                return BadRequest("Failed to create board widget. A widget with this key may already exist.");
            return Ok(created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{appId:guid}/board/{widgetKey}")]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsUpdate)]
    public async Task<IActionResult> UpdateBoardWidget(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromRoute] string widgetKey,
        [FromBody] SnBoardWidgetManifest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to manage board widgets");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound();

        var app = await customApps.GetAppAsync(appId, proj);
        if (app is null)
            return NotFound();

        try
        {
            var updated = await customApps.UpdateBoardWidgetAsync(appId, widgetKey, request);
            if (updated is null)
                return NotFound($"Board widget '{widgetKey}' not found.");
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{appId:guid}/board/{widgetKey}")]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsUpdate)]
    public async Task<IActionResult> DeleteBoardWidget(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromRoute] string widgetKey)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to manage board widgets");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound();

        var app = await customApps.GetAppAsync(appId, proj);
        if (app is null)
            return NotFound();

        var ok = await customApps.DeleteBoardWidgetAsync(appId, widgetKey);
        if (!ok)
            return NotFound($"Board widget '{widgetKey}' not found.");

        return NoContent();
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateApp(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromBody] CustomAppRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to create a custom app");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest("Name and slug are required");

        try
        {
            var app = await customApps.CreateAppAsync(proj, request);
            if (app == null)
                return BadRequest("Failed to create app");

            return CreatedAtAction(nameof(GetApp), new { dev, proj, appId = app.Id }, app);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{appId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsUpdate)]
    public async Task<IActionResult> UpdateApp(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromBody] CustomAppRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to update a custom app");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound();

        try
        {
            app = await customApps.UpdateAppAsync(app, request);
            return Ok(app);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{appId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsDelete)]
    public async Task<IActionResult> DeleteApp(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to delete a custom app");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound();

        var result = await customApps.DeleteAppAsync(appId);
        if (!result)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{appId:guid}/board/payload")]
    public async Task<IActionResult> UpdateBoardPayload(
        [FromRoute] Guid appId,
        [FromBody] UpdateBoardPayloadRequest request,
        CancellationToken cancellationToken
    )
    {
        var app = await customApps.GetAppAsync(appId);
        if (app is null)
            return NotFound("App not found");

        var secret = GetAppSecretFromRequest();
        if (string.IsNullOrWhiteSpace(secret) || !await customApps.ValidateApiSecretAsync(appId, secret, cancellationToken))
            return Unauthorized();

        var validation = customApps.ValidateBoardWidgetPayload(app, request.WidgetKey, request.Payload);
        if (!validation.Valid)
            return BadRequest(validation.Message ?? "Invalid board payload.");

        try
        {
            var updated = await profiles.UpdateBoardItemPayloadAsync(
                new DyUpdateBoardItemPayloadRequest
                {
                    AccountId = request.AccountId,
                    BoardItemId = request.BoardItemId,
                    CustomAppId = appId.ToString(),
                    CustomAppWidgetKey = validation.Widget.Key,
                    Payload = JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(validation.NormalizedPayload))
                },
                cancellationToken: cancellationToken
            );
            return Ok(SnAccountBoardItem.FromProtoValue(updated));
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return NotFound(ex.Status.Detail);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument
                                                || ex.StatusCode == Grpc.Core.StatusCode.FailedPrecondition)
        {
            return BadRequest(ex.Status.Detail);
        }
    }

    [HttpGet("{appId:guid}/secrets")]
    [Authorize]
    public async Task<IActionResult> ListSecrets(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to view app secrets");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound("App not found");

        var secrets = await customApps.GetAppSecretsAsync(appId);
        return Ok(secrets.Select(s => new SecretResponse(
            s.Id.ToString(),
            null,
            s.Description,
            s.ExpiredAt,
            s.Type,
            s.CreatedAt,
            s.UpdatedAt
        )));
    }

    private string? GetAppSecretFromRequest()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(token))
                return token;
        }

        var headerSecret = Request.Headers["X-App-Secret"].ToString().Trim();
        return string.IsNullOrWhiteSpace(headerSecret) ? null : headerSecret;
    }

    [HttpPost("{appId:guid}/secrets")]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsSecretsManage)]
    public async Task<IActionResult> CreateSecret(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromBody] CreateSecretRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to create app secrets");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound("App not found");

        try
        {
            var secret = await customApps.CreateAppSecretAsync(new SnCustomAppSecret
            {
                AppId = appId,
                Description = request.Description,
                ExpiredAt = request.ExpiresIn.HasValue
                    ? NodaTime.SystemClock.Instance.GetCurrentInstant()
                        .Plus(NodaTime.Duration.FromTimeSpan(request.ExpiresIn.Value))
                    : (NodaTime.Instant?)null,
                Type = request.Type
            });

            return CreatedAtAction(
                nameof(GetSecret),
                new { dev, proj, appId, secretId = secret.Id },
                new SecretResponse(
                    secret.Id.ToString(),
                    secret.Secret,
                    secret.Description,
                    secret.ExpiredAt,
                    secret.Type,
                    secret.CreatedAt,
                    secret.UpdatedAt
                )
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{appId:guid}/secrets/{secretId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetSecret(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromRoute] Guid secretId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to view app secrets");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound("App not found");

        var secret = await customApps.GetAppSecretAsync(secretId, appId);
        if (secret == null)
            return NotFound("Secret not found");

        return Ok(new SecretResponse(
            secret.Id.ToString(),
            null,
            secret.Description,
            secret.ExpiredAt,
            secret.Type,
            secret.CreatedAt,
            secret.UpdatedAt
        ));
    }

    [HttpDelete("{appId:guid}/secrets/{secretId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsSecretsManage)]
    public async Task<IActionResult> DeleteSecret(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromRoute] Guid secretId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to delete app secrets");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound("App not found");

        var secret = await customApps.GetAppSecretAsync(secretId, appId);
        if (secret == null)
            return NotFound("Secret not found");

        var result = await customApps.DeleteAppSecretAsync(secretId, appId);
        if (!result)
            return NotFound("Failed to delete secret");

        return NoContent();
    }

    [HttpPost("{appId:guid}/secrets/{secretId:guid}/rotate")]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsSecretsManage)]
    public async Task<IActionResult> RotateSecret(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromRoute] Guid secretId,
        [FromBody] CreateSecretRequest? request = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to rotate app secrets");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound("App not found");

        try
        {
            var secret = await customApps.RotateAppSecretAsync(new SnCustomAppSecret
            {
                Id = secretId,
                AppId = appId,
                Description = request?.Description,
                ExpiredAt = request?.ExpiresIn.HasValue == true
                    ? NodaTime.SystemClock.Instance.GetCurrentInstant()
                        .Plus(NodaTime.Duration.FromTimeSpan(request.ExpiresIn.Value))
                    : (NodaTime.Instant?)null,
                Type = request?.Type ?? CustomAppSecretType.ApiKey
            });

            return Ok(new SecretResponse(
                secret.Id.ToString(),
                secret.Secret,
                secret.Description,
                secret.ExpiredAt,
                secret.Type,
                secret.CreatedAt,
                secret.UpdatedAt
            ));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
