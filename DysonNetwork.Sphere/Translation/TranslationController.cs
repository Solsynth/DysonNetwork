using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Translation;

[ApiController]
[Route("/api/translate")]
public class TranslationController(ITranslationProvider provider, ICacheService cache) : ControllerBase
{
    private const string CacheKeyPrefix = "translation:";

    private static string GenerateCacheKey(string text, string targetLanguage)
    {
        var inputBytes = Encoding.UTF8.GetBytes($"{text}:{targetLanguage}");
        var hashBytes = SHA256.HashData(inputBytes);
        return $"{CacheKeyPrefix}{Convert.ToHexString(hashBytes)}";
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<string>> Translate(
        [FromBody] string text,
        [FromQuery(Name = "from")] string targetLanguage,
        [FromQuery(Name = "to")] string? sourceLanguage
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        if (currentUser.PerkSubscription is null)
            return StatusCode(403, "You need a subscription to use this feature.");

        // Generate cache key
        var cacheKey = GenerateCacheKey(text, targetLanguage);

        // Try to get from cache first
        var (found, cachedResult) = await cache.GetAsyncWithStatus<string>(cacheKey);
        if (found && cachedResult != null)
            return Ok(cachedResult);

        // If not in cache, translate and cache the result
        var result = await provider.Translate(text, targetLanguage);
        if (!string.IsNullOrEmpty(result))
        {
            await cache.SetAsync(cacheKey, result, TimeSpan.FromHours(24)); // Cache for 24 hours
        }

        return result;
    }
}