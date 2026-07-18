using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Registry;
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
    DyProfileService.DyProfileServiceClient profiles,
    DyAuthorizedAppService.DyAuthorizedAppServiceClient authorizedApps,
    RemotePaymentService payment)
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
        string? BoardItemId,
        [MaxLength(128)] string WidgetKey,
        Dictionary<string, object?>? Payload
    );

    public record CreateCustomOrderRequest(
        [MaxLength(1024)] string? Identifier,
        [MaxLength(64)] string? Currency,
        decimal Amount,
        [Range(1, 720)] int ExpirationHours = 24,
        [MaxLength(4096)] string? Remarks = null,
        Dictionary<string, object?>? Meta = null
    );



    [HttpGet]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsCreate)]
    public async Task<IActionResult> ListApps([FromQuery(Name = "dev")] string dev, [FromQuery(Name = "proj")] Guid proj)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyViewer))
            return StatusCode(403, ApiError.Unauthorized("You must be a viewer of the developer to list custom apps", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found", Status = 404 });

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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyViewer))
            return StatusCode(403, ApiError.Unauthorized("You must be a viewer of the developer to list custom apps", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        return Ok(app);
    }

    [HttpGet("{appId:guid}/board")]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsUpdate)]
    public async Task<IActionResult> ListBoardWidgets(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to manage board widgets", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        var widgets = await customApps.GetBoardWidgetsAsync(appId);
        return Ok(widgets);
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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to manage board widgets", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app is null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        try
        {
            var created = await customApps.CreateBoardWidgetAsync(appId, request);
            if (created is null)
                return BadRequest(new ApiError { Code = "DEV_APP_BOARD_WIDGET_CREATE_FAILED", Message = "Failed to create board widget. A widget with this key may already exist.", Status = 400 });
            return Ok(created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "DEV_APP_BOARD_WIDGET_CREATE_FAILED", Message = ex.Message, Status = 400 });
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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to manage board widgets", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app is null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        try
        {
            var updated = await customApps.UpdateBoardWidgetAsync(appId, widgetKey, request);
            if (updated is null)
                return NotFound(new ApiError { Code = "DEV_APP_BOARD_WIDGET_NOT_FOUND", Message = $"Board widget '{widgetKey}' not found.", Status = 404 });
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "DEV_APP_BOARD_WIDGET_UPDATE_FAILED", Message = ex.Message, Status = 400 });
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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to manage board widgets", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app is null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        var ok = await customApps.DeleteBoardWidgetAsync(appId, widgetKey);
        if (!ok)
            return NotFound(new ApiError { Code = "DEV_APP_BOARD_WIDGET_NOT_FOUND", Message = $"Board widget '{widgetKey}' not found.", Status = 404 });

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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to create a custom app", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest(new ApiError { Code = "DEV_APP_NAME_SLUG_REQUIRED", Message = "Name and slug are required", Status = 400 });

        try
        {
            var app = await customApps.CreateAppAsync(proj, request);
            if (app == null)
                return BadRequest(new ApiError { Code = "DEV_APP_CREATE_FAILED", Message = "Failed to create app", Status = 400 });

            return CreatedAtAction(nameof(GetApp), new { dev, proj, appId = app.Id }, app);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "DEV_APP_CREATE_FAILED", Message = ex.Message, Status = 400 });
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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to update a custom app", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        try
        {
            app = await customApps.UpdateAppAsync(app, request);
            return Ok(app);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "DEV_APP_UPDATE_FAILED", Message = ex.Message, Status = 400 });
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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to delete a custom app", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        var result = await customApps.DeleteAppAsync(appId);
        if (!result)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        return NoContent();
    }

    [HttpPost("{appId:guid}/orders")]
    [Authorize]
    [AskPermission(PermissionKeys.CustomAppsUpdate)]
    public async Task<IActionResult> CreateCustomOrder(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromBody] CreateCustomOrderRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to create custom app orders", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app is null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        if (string.IsNullOrWhiteSpace(request.Identifier))
            return BadRequest(new ApiError { Code = "DEV_APP_ORDER_IDENTIFIER_REQUIRED", Message = "Identifier is required", Status = 400 });

        if (string.IsNullOrWhiteSpace(request.Currency))
            return BadRequest(new ApiError { Code = "DEV_APP_ORDER_CURRENCY_REQUIRED", Message = "Currency is required", Status = 400 });

        if (request.Amount < 0.001m)
            return BadRequest(new ApiError { Code = "DEV_APP_ORDER_AMOUNT_TOO_SMALL", Message = "Amount must be at least 0.001", Status = 400 });

        var order = await payment.CreateOrder(
            request.Currency,
            request.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            payeeWalletId: null,
            TimeSpan.FromHours(request.ExpirationHours),
            app.ResourceIdentifier,
            request.Identifier,
            request.Meta is null ? null : InfraObjectCoder.ConvertObjectToByteString(request.Meta).ToByteArray(),
            remarks: request.Remarks,
            reuseable: false);

        return Ok(order);
    }

    [HttpGet("{appId:guid}/board/widgets")]
    public async Task<IActionResult> ListOwnBoardWidgets(
        [FromRoute] Guid appId,
        CancellationToken cancellationToken
    )
    {
        var app = await customApps.GetAppAsync(appId);
        if (app is null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        var secret = GetAppSecretFromRequest();
        if (string.IsNullOrWhiteSpace(secret) || !await customApps.ValidateApiSecretAsync(appId, secret, cancellationToken))
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (app.OauthConfig?.AllowedScopes?.Contains(
                PermissionKeys.AccountsProfileBoard, StringComparer.OrdinalIgnoreCase) != true)
        {
            return StatusCode(403,
                ApiError.Unauthorized($"Custom app must declare '{PermissionKeys.AccountsProfileBoard}' scope to provide board widgets.", forbidden: true));
        }

        var widgets = await customApps.GetBoardWidgetsAsync(appId);
        return Ok(widgets);
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
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        var secret = GetAppSecretFromRequest();
        if (string.IsNullOrWhiteSpace(secret) || !await customApps.ValidateApiSecretAsync(appId, secret, cancellationToken))
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        // App capability: must declare accounts.profile.board in OauthConfig.AllowedScopes.
        // User consent: must have authorized this app with that scope (AuthorizedApp).
        if (app.OauthConfig?.AllowedScopes?.Contains(
                PermissionKeys.AccountsProfileBoard, StringComparer.OrdinalIgnoreCase) != true)
        {
            return StatusCode(403,
                ApiError.Unauthorized($"Custom app must declare '{PermissionKeys.AccountsProfileBoard}' scope to provide board widgets.", forbidden: true));
        }

        if (string.IsNullOrWhiteSpace(request.AccountId) || !Guid.TryParse(request.AccountId, out _))
            return BadRequest(new ApiError { Code = "DEV_APP_BOARD_PAYLOAD_ACCOUNT_ID_REQUIRED", Message = "account_id is required.", Status = 400 });

        var authorized = await authorizedApps.QueryAuthorizedBoardAppsAsync(
            new DyQueryAuthorizedBoardAppsRequest
            {
                AccountId = request.AccountId,
                AppSlug = app.Slug,
                Take = 1,
                Offset = 0
            },
            cancellationToken: cancellationToken);

        var appIdString = appId.ToString();
        if (authorized.Apps.All(a => !string.Equals(a.AppId, appIdString, StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(403,
                ApiError.Unauthorized($"User has not authorized this app with scope '{PermissionKeys.AccountsProfileBoard}'.", forbidden: true));
        }

        var validation = await customApps.ValidateBoardWidgetPayload(app, request.WidgetKey, request.Payload);
        if (!validation.Valid)
            return BadRequest(new ApiError { Code = "DEV_APP_BOARD_PAYLOAD_INVALID", Message = validation.Message ?? "Invalid board payload.", Status = 400 });

        try
        {
            // Protobuf string fields reject null — only set BoardItemId when provided
            // (omit = auto-find first matching board item for account + app + widget).
            var grpcRequest = new DyUpdateBoardItemPayloadRequest
            {
                AccountId = request.AccountId,
                CustomAppId = appId.ToString(),
                CustomAppWidgetKey = validation.Widget.Key,
                Payload = JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(validation.NormalizedPayload))
            };
            if (!string.IsNullOrWhiteSpace(request.BoardItemId))
                grpcRequest.BoardItemId = request.BoardItemId;

            var updated = await profiles.UpdateBoardItemPayloadAsync(
                grpcRequest,
                cancellationToken: cancellationToken
            );
            return Ok(SnAccountBoardItem.FromProtoValue(updated));
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = ex.Status.Detail, Status = 404 });
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument
                                                || ex.StatusCode == Grpc.Core.StatusCode.FailedPrecondition)
        {
            return BadRequest(new ApiError { Code = "DEV_APP_BOARD_PAYLOAD_RPC_ERROR", Message = ex.Status.Detail, Status = 400 });
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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to view app secrets", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to create app secrets", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

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
            return BadRequest(new ApiError { Code = "DEV_APP_SECRET_CREATE_FAILED", Message = ex.Message, Status = 400 });
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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to view app secrets", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        var secret = await customApps.GetAppSecretAsync(secretId, appId);
        if (secret == null)
            return NotFound(new ApiError { Code = "DEV_APP_SECRET_NOT_FOUND", Message = "Secret not found", Status = 404 });

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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to delete app secrets", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

        var secret = await customApps.GetAppSecretAsync(secretId, appId);
        if (secret == null)
            return NotFound(new ApiError { Code = "DEV_APP_SECRET_NOT_FOUND", Message = "Secret not found", Status = 404 });

        var result = await customApps.DeleteAppSecretAsync(secretId, appId);
        if (!result)
            return NotFound(new ApiError { Code = "DEV_APP_SECRET_DELETE_FAILED", Message = "Failed to delete secret", Status = 404 });

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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to rotate app secrets", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound(new ApiError { Code = "DEV_APP_PROJECT_NOT_FOUND", Message = "Project not found or you don't have access", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app == null)
            return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "App not found", Status = 404 });

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
            return BadRequest(new ApiError { Code = "DEV_APP_SECRET_ROTATE_FAILED", Message = ex.Message, Status = 400 });
        }
    }
}
