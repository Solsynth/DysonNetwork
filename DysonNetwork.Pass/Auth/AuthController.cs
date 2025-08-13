using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Pass.Account;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.GeoIp;

namespace DysonNetwork.Pass.Auth;

[ApiController]
[Route("/api/auth")]
public class AuthController(
    AppDatabase db,
    AccountService accounts,
    AuthService auth,
    GeoIpService geo,
    ActionLogService als,
    IConfiguration configuration
) : ControllerBase
{
    private readonly string _cookieDomain = configuration["AuthToken:CookieDomain"]!;
    
    public class ChallengeRequest
    {
        [Required] public ChallengePlatform Platform { get; set; }
        [Required] [MaxLength(256)] public string Account { get; set; } = null!;
        [Required] [MaxLength(512)] public string DeviceId { get; set; } = null!;
        public List<string> Audiences { get; set; } = new();
        public List<string> Scopes { get; set; } = new();
    }

    [HttpPost("challenge")]
    public async Task<ActionResult<AuthChallenge>> StartChallenge([FromBody] ChallengeRequest request)
    {
        var account = await accounts.LookupAccount(request.Account);
        if (account is null) return NotFound("Account was not found.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var punishment = await db.Punishments
            .Where(e => e.AccountId == account.Id)
            .Where(e => e.Type == PunishmentType.BlockLogin || e.Type == PunishmentType.DisableAccount)
            .Where(e => e.ExpiredAt == null || now < e.ExpiredAt)
            .FirstOrDefaultAsync();
        if (punishment is not null) return StatusCode(423, punishment);

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        // Trying to pick up challenges from the same IP address and user agent
        var existingChallenge = await db.AuthChallenges
            .Where(e => e.AccountId == account.Id)
            .Where(e => e.IpAddress == ipAddress)
            .Where(e => e.UserAgent == userAgent)
            .Where(e => e.StepRemain > 0)
            .Where(e => e.ExpiredAt != null && now < e.ExpiredAt)
            .FirstOrDefaultAsync();
        if (existingChallenge is not null) return existingChallenge;

        var device = await auth.GetOrCreateDeviceAsync(account.Id, request.DeviceId);
        var challenge = new AuthChallenge
        {
            ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddHours(1)),
            StepTotal = await auth.DetectChallengeRisk(Request, account),
            Platform = request.Platform,
            Audiences = request.Audiences,
            Scopes = request.Scopes,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Location = geo.GetPointFromIp(ipAddress),
            ClientId = device.Id,
            AccountId = account.Id
        }.Normalize();

        await db.AuthChallenges.AddAsync(challenge);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(ActionLogType.ChallengeAttempt,
            new Dictionary<string, object> { { "challenge_id", challenge.Id } }, Request, account
        );

        return challenge;
    }

    [HttpGet("challenge/{id:guid}")]
    public async Task<ActionResult<AuthChallenge>> GetChallenge([FromRoute] Guid id)
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .ThenInclude(e => e.Profile)
            .FirstOrDefaultAsync(e => e.Id == id);

        return challenge is null
            ? NotFound("Auth challenge was not found.")
            : challenge;
    }

    [HttpGet("challenge/{id:guid}/factors")]
    public async Task<ActionResult<List<AccountAuthFactor>>> GetChallengeFactors([FromRoute] Guid id)
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .Include(e => e.Account.AuthFactors)
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        return challenge is null
            ? NotFound("Auth challenge was not found.")
            : challenge.Account.AuthFactors.Where(e => e is { EnabledAt: not null, Trustworthy: >= 1 }).ToList();
    }

    [HttpPost("challenge/{id:guid}/factors/{factorId:guid}")]
    public async Task<ActionResult> RequestFactorCode(
        [FromRoute] Guid id,
        [FromRoute] Guid factorId,
        [FromBody] string? hint
    )
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .Where(e => e.Id == id).FirstOrDefaultAsync();
        if (challenge is null) return NotFound("Auth challenge was not found.");
        var factor = await db.AccountAuthFactors
            .Where(e => e.Id == factorId)
            .Where(e => e.Account == challenge.Account).FirstOrDefaultAsync();
        if (factor is null) return NotFound("Auth factor was not found.");

        try
        {
            await accounts.SendFactorCode(challenge.Account, factor, hint);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        return Ok();
    }

    public class PerformChallengeRequest
    {
        [Required] public Guid FactorId { get; set; }
        [Required] public string Password { get; set; } = string.Empty;
    }

    [HttpPatch("challenge/{id:guid}")]
    public async Task<ActionResult<AuthChallenge>> DoChallenge(
        [FromRoute] Guid id,
        [FromBody] PerformChallengeRequest request
    )
    {
        var challenge = await db.AuthChallenges.Include(e => e.Account).FirstOrDefaultAsync(e => e.Id == id);
        if (challenge is null) return NotFound("Auth challenge was not found.");

        var factor = await db.AccountAuthFactors.FindAsync(request.FactorId);
        if (factor is null) return NotFound("Auth factor was not found.");
        if (factor.EnabledAt is null) return BadRequest("Auth factor is not enabled.");
        if (factor.Trustworthy <= 0) return BadRequest("Auth factor is not trustworthy.");

        if (challenge.StepRemain == 0) return challenge;
        if (challenge.ExpiredAt.HasValue && challenge.ExpiredAt.Value < Instant.FromDateTimeUtc(DateTime.UtcNow))
            return BadRequest();

        try
        {
            if (await accounts.VerifyFactorCode(factor, request.Password))
            {
                challenge.StepRemain -= factor.Trustworthy;
                challenge.StepRemain = Math.Max(0, challenge.StepRemain);
                challenge.BlacklistFactors.Add(factor.Id);
                db.Update(challenge);
                als.CreateActionLogFromRequest(ActionLogType.ChallengeSuccess,
                    new Dictionary<string, object>
                    {
                        { "challenge_id", challenge.Id },
                        { "factor_id", factor.Id }
                    }, Request, challenge.Account
                );
            }
            else
            {
                throw new ArgumentException("Invalid password.");
            }
        }
        catch
        {
            challenge.FailedAttempts++;
            db.Update(challenge);
            als.CreateActionLogFromRequest(ActionLogType.ChallengeFailure,
                new Dictionary<string, object>
                {
                    { "challenge_id", challenge.Id },
                    { "factor_id", factor.Id }
                }, Request, challenge.Account
            );
            await db.SaveChangesAsync();
            return BadRequest("Invalid password.");
        }

        if (challenge.StepRemain == 0)
        {
            als.CreateActionLogFromRequest(ActionLogType.NewLogin,
                new Dictionary<string, object>
                {
                    { "challenge_id", challenge.Id },
                    { "account_id", challenge.AccountId }
                }, Request, challenge.Account
            );
        }

        await db.SaveChangesAsync();
        return challenge;
    }

    public class TokenExchangeRequest
    {
        public string GrantType { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public string? Code { get; set; }
    }

    public class TokenExchangeResponse
    {
        public string Token { get; set; } = string.Empty;
    }

    [HttpPost("token")]
    public async Task<ActionResult<TokenExchangeResponse>> ExchangeToken([FromBody] TokenExchangeRequest request)
    {
        switch (request.GrantType)
        {
            case "authorization_code":
                var code = Guid.TryParse(request.Code, out var codeId) ? codeId : Guid.Empty;
                if (code == Guid.Empty)
                    return BadRequest("Invalid or missing authorization code.");
                var challenge = await db.AuthChallenges
                    .Include(e => e.Account)
                    .Where(e => e.Id == code)
                    .FirstOrDefaultAsync();
                if (challenge is null)
                    return BadRequest("Authorization code not found or expired.");
                if (challenge.StepRemain != 0)
                    return BadRequest("Challenge not yet completed.");

                var session = await db.AuthSessions
                    .Where(e => e.Challenge == challenge)
                    .FirstOrDefaultAsync();
                if (session is not null)
                    return BadRequest("Session already exists for this challenge.");

                session = new AuthSession
                {
                    LastGrantedAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
                    ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(30)),
                    Account = challenge.Account,
                    Challenge = challenge,
                };

                db.AuthSessions.Add(session);
                await db.SaveChangesAsync();

                var tk = auth.CreateToken(session);
                Response.Cookies.Append(AuthConstants.CookieTokenName, tk, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    Domain = _cookieDomain,
                    Expires = DateTime.UtcNow.AddDays(30)
                });

                return Ok(new TokenExchangeResponse { Token = tk });
            default:
                // Since we no longer need the refresh token
                // This case is blank for now, thinking to mock it if the OIDC standard requires it
                return BadRequest("Unsupported grant type.");
        }
    }

    [HttpPost("captcha")]
    public async Task<ActionResult> ValidateCaptcha([FromBody] string token)
    {
        var result = await auth.ValidateCaptcha(token);
        return result ? Ok() : BadRequest();
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(AuthConstants.CookieTokenName, new CookieOptions
        {
            Domain = _cookieDomain,
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax
        });
        return Ok();
    }
}