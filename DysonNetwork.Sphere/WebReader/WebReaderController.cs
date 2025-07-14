using DysonNetwork.Sphere.Permission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DysonNetwork.Sphere.WebReader;

/// <summary>
/// Controller for web scraping and link preview services
/// </summary>
[ApiController]
[Route("/api/scrap")]
[EnableRateLimiting("fixed")]
public class WebReaderController(WebReaderService reader, ILogger<WebReaderController> logger)
    : ControllerBase
{
    /// <summary>
    /// Retrieves a preview for the provided URL
    /// </summary>
    /// <param name="url">URL-encoded link to generate preview for</param>
    /// <returns>Link preview data including title, description, and image</returns>
    [HttpGet("link")]
    public async Task<ActionResult<LinkEmbed>> ScrapLink([FromQuery] string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return BadRequest(new { error = "URL parameter is required" });
        }

        try
        {
            // Ensure URL is properly decoded
            var decodedUrl = UrlDecoder.Decode(url);

            // Validate URL format
            if (!Uri.TryCreate(decodedUrl, UriKind.Absolute, out _))
            {
                return BadRequest(new { error = "Invalid URL format" });
            }

            var linkEmbed = await reader.GetLinkPreviewAsync(decodedUrl);
            return Ok(linkEmbed);
        }
        catch (WebReaderException ex)
        {
            logger.LogWarning(ex, "Error scraping link: {Url}", url);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error scraping link: {Url}", url);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { error = "An unexpected error occurred while processing the link" });
        }
    }

    /// <summary>
    /// Force invalidates the cache for a specific URL
    /// </summary>
    [HttpDelete("link/cache")]
    [Authorize]
    [RequiredPermission("maintenance", "cache.scrap")]
    public async Task<IActionResult> InvalidateCache([FromQuery] string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return BadRequest(new { error = "URL parameter is required" });
        }

        await reader.InvalidateCacheForUrlAsync(url);
        return Ok(new { message = "Cache invalidated for URL" });
    }

    /// <summary>
    /// Force invalidates all cached link previews
    /// </summary>
    [HttpDelete("cache/all")]
    [Authorize]
    [RequiredPermission("maintenance", "cache.scrap")]
    public async Task<IActionResult> InvalidateAllCache()
    {
        await reader.InvalidateAllCachedPreviewsAsync();
        return Ok(new { message = "All link preview caches invalidated" });
    }
}

/// <summary>
/// Helper class for URL decoding
/// </summary>
public static class UrlDecoder
{
    public static string Decode(string url)
    {
        // First check if URL is already decoded
        if (!url.Contains('%') && !url.Contains('+'))
        {   
            return url;
        }

        try
        {
            return System.Net.WebUtility.UrlDecode(url);
        }
        catch
        {
            // If decoding fails, return the original string
            return url;
        }
    }
}