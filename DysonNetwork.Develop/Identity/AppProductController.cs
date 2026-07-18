using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/private/apps/{appId:guid}/products")]
public class AppProductController(
    AppProductService productService,
    CustomAppService customApps,
    DeveloperService ds,
    DevProjectService projectService
) : ControllerBase
{
    public record ProductFulfillmentRequest(
        bool? IsAddressRequired,
        List<string>? RequiredScopes
    );

    public record ProductStateRequest(
        bool? IsEnabled,
        string? StockMode,
        int? StockQuantity
    );

    public record ProductRequest(
        [MaxLength(1024)] string? Identifier,
        [MaxLength(1024)] string? DisplayName,
        [MaxLength(4096)] string? Description,
        [MaxLength(128)] string? Currency,
        decimal? Price,
        string? PictureId,
        string? BackgroundId,
        string? Recurrence,
        string? GroupIdentifier,
        ProductFulfillmentRequest? Fulfillment,
        ProductStateRequest? State
    );

    private async Task<IActionResult> ResolveAppAsync(string dev, Guid proj, Guid appId, string role)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound(new ApiError { Code = "APP_PRODUCT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId,
                role == "editor" ? DyPublisherMemberRole.DyEditor : DyPublisherMemberRole.DyViewer))
            return StatusCode(403, ApiError.Unauthorized($"You must be a {role} of the developer.", forbidden: true));

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound(new ApiError { Code = "APP_PRODUCT_PROJECT_NOT_FOUND", Message = "Project not found", Status = 404 });

        var app = await customApps.GetAppAsync(appId, proj);
        if (app is null) return NotFound(new ApiError { Code = "APP_PRODUCT_NOT_FOUND", Message = "App not found", Status = 404 });

        return Ok((developer, project, app));
    }

    [HttpGet]
    [Authorize]
    [AskPermission(PermissionKeys.AppProductsCreate)]
    public async Task<IActionResult> ListProducts(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId)
    {
        var resolved = await ResolveAppAsync(dev, proj, appId, "viewer");
        if (resolved is not OkObjectResult ok) return resolved;

        var products = await productService.GetProductsByAppAsync(appId);
        return Ok(products);
    }

    [HttpGet("{productId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetProduct(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromRoute] Guid productId)
    {
        var resolved = await ResolveAppAsync(dev, proj, appId, "viewer");
        if (resolved is not OkObjectResult) return resolved;

        var product = await productService.GetProductAsync(productId, appId);
        if (product is null) return NotFound(new ApiError { Code = "APP_PRODUCT_NOT_FOUND", Message = "Product not found", Status = 404 });

        return Ok(product);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateProduct(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromBody] ProductRequest request)
    {
        var resolved = await ResolveAppAsync(dev, proj, appId, "editor");
        if (resolved is not OkObjectResult) return resolved;

        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.DisplayName) ||
            string.IsNullOrWhiteSpace(request.Currency))
            return BadRequest(new ApiError { Code = "APP_PRODUCT_FIELD_REQUIRED", Message = "Identifier, displayName, and currency are required.", Status = 400 });

        var existing = await productService.GetProductByIdentifierAsync(appId, request.Identifier);
        if (existing is not null)
            return Conflict(ApiError.Conflict("A product with this identifier already exists.", code: "APP_PRODUCT_IDENTIFIER_CONFLICT"));

        var product = new SnAppProduct
        {
            Identifier = request.Identifier,
            DisplayName = request.DisplayName,
            Description = request.Description,
            Currency = request.Currency,
            Price = request.Price ?? 0,
            Recurrence = ParseRecurrence(request.Recurrence),
            GroupIdentifier = request.GroupIdentifier,
            Fulfillment = request.Fulfillment is null ? null : new SnAppProductFulfillment
            {
                IsAddressRequired = request.Fulfillment.IsAddressRequired ?? false,
                RequiredScopes = request.Fulfillment.RequiredScopes?.Distinct(StringComparer.Ordinal).ToArray() ?? []
            },
            State = BuildState(request.State)
        };

        if (request.PictureId is not null)
            product.Picture = await productService.ResolveFileAsync(request.PictureId);
        if (request.BackgroundId is not null)
            product.Background = await productService.ResolveFileAsync(request.BackgroundId);

        product = await productService.CreateProductAsync(appId, product);
        return CreatedAtAction(nameof(GetProduct), new { dev, proj, appId, productId = product.Id }, product);
    }

    [HttpPatch("{productId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.AppProductsUpdate)]
    public async Task<IActionResult> UpdateProduct(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromRoute] Guid productId,
        [FromBody] ProductRequest request)
    {
        var resolved = await ResolveAppAsync(dev, proj, appId, "editor");
        if (resolved is not OkObjectResult) return resolved;

        var product = await productService.GetProductAsync(productId, appId);
        if (product is null) return NotFound(new ApiError { Code = "APP_PRODUCT_NOT_FOUND", Message = "Product not found", Status = 404 });

        if (request.Identifier is not null) product.Identifier = request.Identifier;
        if (request.DisplayName is not null) product.DisplayName = request.DisplayName;
        if (request.Description is not null) product.Description = request.Description;
        if (request.Currency is not null) product.Currency = request.Currency;
        if (request.Price.HasValue) product.Price = request.Price.Value;
        if (request.Recurrence is not null) product.Recurrence = ParseRecurrence(request.Recurrence);
        if (request.GroupIdentifier is not null) product.GroupIdentifier = request.GroupIdentifier;
        if (request.Fulfillment is not null)
        {
            product.Fulfillment ??= new SnAppProductFulfillment();
            if (request.Fulfillment.IsAddressRequired.HasValue)
                product.Fulfillment.IsAddressRequired = request.Fulfillment.IsAddressRequired.Value;
            if (request.Fulfillment.RequiredScopes is not null)
                product.Fulfillment.RequiredScopes = request.Fulfillment.RequiredScopes.Distinct(StringComparer.Ordinal).ToArray();
        }
        if (request.State is not null)
        {
            product.State ??= new SnAppProductState { ProductId = product.Id };
            if (request.State.IsEnabled.HasValue)
                product.State.IsEnabled = request.State.IsEnabled.Value;
            if (request.State.StockMode is not null)
                product.State.StockMode = ParseStockMode(request.State.StockMode);
            if (request.State.StockQuantity.HasValue)
            {
                product.State.StockQuantity = request.State.StockQuantity.Value;
                if (product.State.StockMode == ProductStockMode.Manual)
                {
                    product.State.LastRestockedAt = NodaTime.SystemClock.Instance.GetCurrentInstant();
                    product.State.LastRestockedQuantity = request.State.StockQuantity.Value;
                }
            }
        }
        if (request.PictureId is not null)
            product.Picture = await productService.ResolveFileAsync(request.PictureId);
        if (request.BackgroundId is not null)
            product.Background = await productService.ResolveFileAsync(request.BackgroundId);

        product = await productService.UpdateProductAsync(product);
        return Ok(product);
    }

    [HttpDelete("{productId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.AppProductsDelete)]
    public async Task<IActionResult> DeleteProduct(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid appId,
        [FromRoute] Guid productId)
    {
        var resolved = await ResolveAppAsync(dev, proj, appId, "editor");
        if (resolved is not OkObjectResult) return resolved;

        var result = await productService.DeleteProductAsync(productId, appId);
        if (!result) return NotFound(new ApiError { Code = "APP_PRODUCT_NOT_FOUND", Message = "Product not found", Status = 404 });

        return NoContent();
    }

    private static SnAppProductState BuildState(ProductStateRequest? request)
    {
        var state = new SnAppProductState
        {
            IsEnabled = request?.IsEnabled ?? true,
            StockMode = ParseStockMode(request?.StockMode),
            StockQuantity = request?.StockQuantity
        };

        if (state.StockMode == ProductStockMode.Manual && request?.StockQuantity is not null)
        {
            state.LastRestockedAt = NodaTime.SystemClock.Instance.GetCurrentInstant();
            state.LastRestockedQuantity = request.StockQuantity.Value;
        }

        return state;
    }

    private static ProductRecurrence ParseRecurrence(string? value) => value?.ToLowerInvariant() switch
    {
        "weekly" => ProductRecurrence.Weekly,
        "monthly" => ProductRecurrence.Monthly,
        "yearly" => ProductRecurrence.Yearly,
        _ => ProductRecurrence.None
    };

    private static ProductStockMode ParseStockMode(string? value) => value?.ToLowerInvariant() switch
    {
        "daily" => ProductStockMode.Daily,
        "weekly" => ProductStockMode.Weekly,
        "monthly" => ProductStockMode.Monthly,
        "yearly" => ProductStockMode.Yearly,
        "manual" => ProductStockMode.Manual,
        _ => ProductStockMode.Unlimited
    };
}
