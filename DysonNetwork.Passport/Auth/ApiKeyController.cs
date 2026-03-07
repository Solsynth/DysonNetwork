using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Auth;

[ApiController]
[Route("/api/auth/keys")]
public class ApiKeyController(AppDatabase db, AuthService auth) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetKeys([FromQuery] int offset = 0, [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        offset = Math.Max(offset, 0);
        take = Math.Clamp(take, 1, 100);

        var query = db.ApiKeys
            .Where(e => e.AccountId == currentUser.Id)
            .AsNoTracking()
            .OrderByDescending(e => e.UpdatedAt);

        var totalCount = await query.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var keys = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        return Ok(keys);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetKey(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var key = await db.ApiKeys
            .AsNoTracking()
            .Where(e => e.AccountId == currentUser.Id)
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (key == null) return NotFound();
        return Ok(key);
    }

    public class ApiKeyRequest
    {
        [MaxLength(1024)] public string? Label { get; set; }
        public Instant? ExpiredAt { get; set; }
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateKey([FromBody] ApiKeyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
            return BadRequest("Label is required");
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (request.ExpiredAt.HasValue && request.ExpiredAt <= SystemClock.Instance.GetCurrentInstant())
            return BadRequest("ExpiredAt must be in the future.");

        try
        {
            var key = await auth.CreateApiKey(currentUser.Id, request.Label, request.ExpiredAt);
            key.Key = await auth.IssueApiKeyToken(key);
            return Ok(key);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/rotate")]
    [Authorize]
    public async Task<IActionResult> RotateKey(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        
        var key = await auth.GetApiKey(id, currentUser.Id);
        if(key is null) return NotFound();
        try
        {
            key = await auth.RotateApiKeyToken(key);
            key.Key = await auth.IssueApiKeyToken(key);
            return Ok(key);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteKey(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        
        var key = await auth.GetApiKey(id, currentUser.Id);
        if(key is null) return NotFound();
        try
        {
            await auth.RevokeApiKeyToken(key);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
