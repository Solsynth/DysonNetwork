using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("api/apps")]
public class CustomAppPublicController(
    CustomAppService customAppService,
    DeveloperService developerService,
    AppProductService productService
) : ControllerBase
{
    public record CustomAppDiscoveryResponse(
        Guid Id,
        string Slug,
        string Title,
        string? Description,
        int ProductsCount,
        int WidgetsCount
    );

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CustomAppDiscoveryResponse>>> DiscoverApps(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null)
    {
        var (apps, total) = await customAppService.GetActiveAppsForDiscoveryAsync(take, offset, search);
        Response.Headers.Append("X-Total", total.ToString());
        return Ok(apps);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<SnCustomApp>> GetCustomAppBySlug([FromRoute] string slug)
    {
        var app = await customAppService.GetAppBySlugAsync(slug);
        if (app is null) return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "Custom app not found", Status = 404 });

        var developer = await developerService.GetDeveloperById(app.Project.DeveloperId);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_APP_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });
        app.Project.Developer = await developerService.LoadDeveloperPublisher(developer);

        return Ok(app);
    }

    [HttpGet("{slug}/products")]
    public async Task<ActionResult<IEnumerable<SnAppProduct>>> GetAppProducts([FromRoute] string slug)
    {
        var app = await customAppService.GetAppBySlugAsync(slug);
        if (app is null) return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "Custom app not found", Status = 404 });

        var products = await productService.GetProductsByAppAsync(app.Id);
        return Ok(products);
    }

    [HttpGet("{slug}/products/{identifier}")]
    public async Task<ActionResult<SnAppProduct>> GetAppProductByIdentifier(
        [FromRoute] string slug,
        [FromRoute] string identifier)
    {
        var app = await customAppService.GetAppBySlugAsync(slug);
        if (app is null) return NotFound(new ApiError { Code = "DEV_APP_NOT_FOUND", Message = "Custom app not found", Status = 404 });

        var product = await productService.GetProductByIdentifierAsync(app.Id, identifier);
        if (product is null) return NotFound(new ApiError { Code = "DEV_APP_PRODUCT_NOT_FOUND", Message = "Product not found", Status = 404 });

        return Ok(product);
    }
}
