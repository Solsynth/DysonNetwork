using System.ComponentModel.DataAnnotations;
using DysonNetwork.Padlock.Auth;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Padlock.Account;

[ApiController]
[Route("/api/accounts")]
public class AccountController(
    AuthService auth,
    AccountService accounts,
    GeoService geo
) : ControllerBase
{
    public class AccountCreateRequest
    {
        [Required]
        [MinLength(2)]
        [MaxLength(256)]
        [RegularExpression(@"^[A-Za-z0-9_-]+$",
            ErrorMessage = "Name can only contain letters, numbers, underscores, and hyphens.")]
        public string Name { get; set; } = string.Empty;

        [Required] [MaxLength(256)] public string Nick { get; set; } = string.Empty;

        [EmailAddress]
        [RegularExpression(@"^[^+]+@[^@]+\.[^@]+$", ErrorMessage = "Email address cannot contain '+' symbol.")]
        [Required]
        [MaxLength(1024)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(4)]
        [MaxLength(128)]
        public string Password { get; set; } = string.Empty;

        [MaxLength(32)] public string Language { get; set; } = "en-us";
        [Required] public string CaptchaToken { get; set; } = string.Empty;
    }

    public class AccountCreateValidateRequest
    {
        [MinLength(2)]
        [MaxLength(256)]
        [RegularExpression(@"^[A-Za-z0-9_-]+$",
            ErrorMessage = "Name can only contain letters, numbers, underscores, and hyphens.")]
        public string? Name { get; set; }

        [EmailAddress]
        [RegularExpression(@"^[^+]+@[^@]+\.[^@]+$", ErrorMessage = "Email address cannot contain '+' symbol.")]
        [MaxLength(1024)]
        public string? Email { get; set; }
    }

    [HttpPost("validate")]
    public async Task<ActionResult<string>> ValidateCreateAccountRequest([FromBody] AccountCreateValidateRequest request)
    {
        if (request.Name is not null && await accounts.CheckAccountNameHasTaken(request.Name))
            return BadRequest("Account name has already been taken.");
        if (request.Email is not null && await accounts.CheckEmailHasBeenUsed(request.Email))
            return BadRequest("Email has already been used.");
        return Ok("Everything seems good.");
    }

    [HttpPost]
    public async Task<ActionResult<SnAccount>> CreateAccount([FromBody] AccountCreateRequest request)
    {
        if (!await auth.ValidateCaptcha(request.CaptchaToken))
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.CaptchaToken)] = ["Invalid captcha token."]
            }, traceId: HttpContext.TraceIdentifier));

        var ip = HttpContext.GetClientIpAddress();
        var region = ip is null ? "us" : geo.GetFromIp(ip)?.Country.IsoCode ?? "us";

        try
        {
            var account = await accounts.CreateAccount(
                request.Name,
                request.Nick,
                request.Email,
                request.Password,
                request.Language,
                region
            );
            return Ok(account);
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError
            {
                Code = "BAD_REQUEST",
                Message = "Failed to create account.",
                Detail = ex.Message,
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }
}
