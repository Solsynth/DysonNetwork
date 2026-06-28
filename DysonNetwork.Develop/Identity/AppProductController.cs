using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/developers/{pubName}/projects/{projectId:guid}/apps/{appId:guid}/products")]
public class AppProductController(
    AppProductService productService,
    CustomAppService customApps,
    DeveloperService ds,
    DevProjectService projectService
) : ControllerBase
{
    public record ProductRequest(
        [MaxLength(1024)] string? Identifier,
        [MaxLength(1024)] string? DisplayName,
        [MaxLength(4096)] string? Description,
        [MaxLength(128)] string? Currency,
        decimal? Price,
        string? PictureId,
        string? BackgroundId
    );

    private async Task<IActionResult> ResolveAppAsync(string pubName, Guid projectId, Guid appId, string role)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null) return NotFound("Developer not found");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId,
                role == "editor" ? DyPublisherMemberRole.DyEditor : DyPublisherMemberRole.DyViewer))
            return StatusCode(403, $"You must be a {role} of the developer.");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null) return NotFound("Project not found");

        var app = await customApps.GetAppAsync(appId, projectId);
        if (app is null) return NotFound("App not found");

        return Ok((developer, project, app));
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ListProducts([FromRoute] string pubName, [FromRoute] Guid projectId,
        [FromRoute] Guid appId)
    {
        var resolved = await ResolveAppAsync(pubName, projectId, appId, "viewer");
        if (resolved is not OkObjectResult ok) return resolved;

        var products = await productService.GetProductsByAppAsync(appId);
        return Ok(products);
    }

    [HttpGet("{productId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetProduct([FromRoute] string pubName, [FromRoute] Guid projectId,
        [FromRoute] Guid appId, [FromRoute] Guid productId)
    {
        var resolved = await ResolveAppAsync(pubName, projectId, appId, "viewer");
        if (resolved is not OkObjectResult) return resolved;

        var product = await productService.GetProductAsync(productId, appId);
        if (product is null) return NotFound();

        return Ok(product);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateProduct([FromRoute] string pubName, [FromRoute] Guid projectId,
        [FromRoute] Guid appId, [FromBody] ProductRequest request)
    {
        var resolved = await ResolveAppAsync(pubName, projectId, appId, "editor");
        if (resolved is not OkObjectResult) return resolved;

        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.DisplayName) ||
            string.IsNullOrWhiteSpace(request.Currency))
            return BadRequest("Identifier, displayName, and currency are required.");

        var existing = await productService.GetProductByIdentifierAsync(appId, request.Identifier);
        if (existing is not null)
            return Conflict("A product with this identifier already exists.");

        var product = new SnAppProduct
        {
            Identifier = request.Identifier,
            DisplayName = request.DisplayName,
            Description = request.Description,
            Currency = request.Currency,
            Price = request.Price ?? 0,
        };

        if (request.PictureId is not null)
            product.Picture = await productService.ResolveFileAsync(request.PictureId);
        if (request.BackgroundId is not null)
            product.Background = await productService.ResolveFileAsync(request.BackgroundId);

        product = await productService.CreateProductAsync(appId, product);
        return CreatedAtAction(nameof(GetProduct),
            new { pubName, projectId, appId, productId = product.Id }, product);
    }

    [HttpPatch("{productId:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateProduct([FromRoute] string pubName, [FromRoute] Guid projectId,
        [FromRoute] Guid appId, [FromRoute] Guid productId, [FromBody] ProductRequest request)
    {
        var resolved = await ResolveAppAsync(pubName, projectId, appId, "editor");
        if (resolved is not OkObjectResult) return resolved;

        var product = await productService.GetProductAsync(productId, appId);
        if (product is null) return NotFound();

        if (request.Identifier is not null) product.Identifier = request.Identifier;
        if (request.DisplayName is not null) product.DisplayName = request.DisplayName;
        if (request.Description is not null) product.Description = request.Description;
        if (request.Currency is not null) product.Currency = request.Currency;
        if (request.Price.HasValue) product.Price = request.Price.Value;
        if (request.PictureId is not null)
            product.Picture = await productService.ResolveFileAsync(request.PictureId);
        if (request.BackgroundId is not null)
            product.Background = await productService.ResolveFileAsync(request.BackgroundId);

        product = await productService.UpdateProductAsync(product);
        return Ok(product);
    }

    [HttpDelete("{productId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteProduct([FromRoute] string pubName, [FromRoute] Guid projectId,
        [FromRoute] Guid appId, [FromRoute] Guid productId)
    {
        var resolved = await ResolveAppAsync(pubName, projectId, appId, "editor");
        if (resolved is not OkObjectResult) return resolved;

        var result = await productService.DeleteProductAsync(productId, appId);
        if (!result) return NotFound();

        return NoContent();
    }

}
