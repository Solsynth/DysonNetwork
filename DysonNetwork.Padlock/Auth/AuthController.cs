using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DysonNetwork.Padlock.Auth;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[Route("/api/auth")]
public class AuthController(
    AuthService auth,
    AppDatabase db,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuthController> logger
) : ControllerBase
{
    private HttpContext HttpContext => httpContextAccessor.HttpContext!;

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        return Ok();
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        return Ok();
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser(CancellationToken ct)
    {
        var user = HttpContext.Items["CurrentUser"] as SnAccount;
        if (user is null) return Unauthorized();
        return Ok(new { user.Id });
    }

    [HttpPost("sudo")]
    [Authorize]
    public async Task<IActionResult> EnableSudoMode([FromBody] SudoRequest request, CancellationToken ct)
    {
        var session = HttpContext.Items["CurrentSession"] as SnAuthSession;
        if (session is null) return Unauthorized();
        
        var valid = await auth.ValidateSudoMode(session, request.PinCode);
        if (!valid) return BadRequest(new { error = "Invalid PIN code" });
        
        return Ok();
    }
}

public record LoginRequest(string? Identifier, string? Password, string? DeviceId);
public record RefreshRequest(string RefreshToken);
public record SudoRequest(string? PinCode);
public class TokenExchangeResponse
{
    public string Token { get; set; } = string.Empty;
    public string? CookieDomain { get; set; }
    public bool? IsSecure { get; set; }
}
