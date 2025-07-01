using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Connection;

[ApiController]
[Route("completion")]
public class AutoCompletionController(AppDatabase db)
    : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AutoCompletionResponse>> GetCompletions([FromBody] AutoCompletionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Content))
        {
            return BadRequest("Content is required");
        }

        var result = new AutoCompletionResponse();
        var lastWord = request.Content.Trim().Split(' ').LastOrDefault() ?? string.Empty;

        if (lastWord.StartsWith("@"))
        {
            var searchTerm = lastWord[1..]; // Remove the @
            result.Items = await GetAccountCompletions(searchTerm);
            result.Type = "account";
        }
        else if (lastWord.StartsWith(":"))
        {
            var searchTerm = lastWord[1..]; // Remove the :
            result.Items = await GetStickerCompletions(searchTerm);
            result.Type = "sticker";
        }

        return Ok(result);
    }

    private async Task<List<CompletionItem>> GetAccountCompletions(string searchTerm)
    {
        return await db.Accounts
            .Where(a => EF.Functions.ILike(a.Name, $"%{searchTerm}%"))
            .OrderBy(a => a.Name)
            .Take(10)
            .Select(a => new CompletionItem
            {
                Id = a.Id.ToString(),
                DisplayName = a.Name,
                SecondaryText = a.Nick,
                Type = "account",
                Data = a
            })
            .ToListAsync();
    }

    private async Task<List<CompletionItem>> GetStickerCompletions(string searchTerm)
    {
        return await db.Stickers
            .Include(s => s.Pack)
            .Where(s => EF.Functions.ILike(s.Pack.Prefix + s.Slug, $"%{searchTerm}%"))
            .OrderBy(s => s.Slug)
            .Take(10)
            .Select(s => new CompletionItem
            {
                Id = s.Id.ToString(),
                DisplayName = s.Slug,
                Type = "sticker",
                Data = s
            })
            .ToListAsync();
    }
}

public class AutoCompletionRequest
{
    [Required] public string Content { get; set; } = string.Empty;
}

public class AutoCompletionResponse
{
    public string Type { get; set; } = string.Empty;
    public List<CompletionItem> Items { get; set; } = new();
}

public class CompletionItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? SecondaryText { get; set; }
    public string Type { get; set; } = string.Empty;
    public object? Data { get; set; }
}