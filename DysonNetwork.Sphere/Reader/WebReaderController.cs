using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Reader;

[ApiController]
[Route("/api/scrap")]
public class WebReaderController(WebReaderService reader, ILogger<WebReaderController> logger)
    : ControllerBase
{
    [HttpGet("link")]
    public async Task<ActionResult<LinkEmbed>> ScrapLink([FromQuery] string url)
    {
        if (string.IsNullOrEmpty(url))
            return BadRequest(new ApiError { Code = "SCRAP_URL_REQUIRED", Message = "URL parameter is required.", Status = 400 });

        try
        {
            var decodedUrl = UrlDecoder.Decode(url);

            if (!Uri.TryCreate(decodedUrl, UriKind.Absolute, out _))
                return BadRequest(new ApiError { Code = "SCRAP_URL_INVALID_FORMAT", Message = "Invalid URL format.", Status = 400 });

            var linkEmbed = await reader.GetLinkPreviewAsync(decodedUrl);
            return Ok(linkEmbed);
        }
        catch (WebReaderException ex)
        {
            logger.LogWarning(ex, "Error scraping link: {Url}", url);
            return BadRequest(new ApiError { Code = "SCRAP_LINK_ERROR", Message = ex.Message, Status = 400 });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error scraping link: {Url}", url);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiError { Code = "SCRAP_UNEXPECTED_ERROR", Message = "An unexpected error occurred while processing the link.", Status = 500 }
            );
        }
    }

    [HttpDelete("link/cache")]
    [Authorize]
    [AskPermission("cache.scrap")]
    public async Task<IActionResult> InvalidateCache([FromQuery] string url)
    {
        if (string.IsNullOrEmpty(url))
            return BadRequest(new ApiError { Code = "SCRAP_URL_REQUIRED", Message = "URL parameter is required.", Status = 400 });

        await reader.InvalidateCacheForUrlAsync(url);
        return Ok(new { message = "Cache invalidated for URL" });
    }

    [HttpDelete("cache/all")]
    [Authorize]
    [AskPermission("cache.scrap")]
    public async Task<IActionResult> InvalidateAllCache()
    {
        await reader.InvalidateAllCachedPreviewsAsync();
        return Ok(new { message = "All link preview caches invalidated" });
    }
}

public static class UrlDecoder
{
    public static string Decode(string url)
    {
        if (!url.Contains('%') && !url.Contains('+'))
            return url;

        try
        {
            return System.Net.WebUtility.UrlDecode(url);
        }
        catch
        {
            return url;
        }
    }
}
