using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Pass.Localization;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Localization;
using AccountService = DysonNetwork.Pass.Account.AccountService;
using ActionLogService = DysonNetwork.Pass.Account.ActionLogService;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Pass.Auth;

[ApiController]
[Route("/api/auth")]
public class AuthController(
    AppDatabase db,
    AccountService accounts,
    AuthService auth,
    GeoService geo,
    ActionLogService als,
    RingService.RingServiceClient pusher,
    IConfiguration configuration,
    ILocalizationService localizer,
    ILogger<AuthController> logger
) : ControllerBase
{
    private readonly string _cookieDomain = configuration["AuthToken:CookieDomain"]!;

    public class ChallengeRequest
    {
        [Required] public Shared.Models.ClientPlatform Platform { get; set; }
        [Required] [MaxLength(256)] public string Account { get; set; } = null!;
        [Required] [MaxLength(512)] public string DeviceId { get; set; } = null!;
        [MaxLength(1024)] public string? DeviceName { get; set; }
        public List<string> Audiences { get; set; } = [];
        public List<string> Scopes { get; set; } = [];
    }

    [HttpPost("challenge")]
    public async Task<ActionResult<SnAuthChallenge>> CreateChallenge([FromBody] ChallengeRequest request)
    {
        var account = await accounts.LookupAccount(request.Account);
        if (account is null) return NotFound("Account was not found.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var punishment = await db.Punishments
            .Where(e => e.AccountId == account.Id)
            .Where(e => e.Type == PunishmentType.BlockLogin || e.Type == PunishmentType.DisableAccount)
            .Where(e => e.ExpiredAt == null || now < e.ExpiredAt)
            .FirstOrDefaultAsync();
        if (punishment is not null)
            return StatusCode(
                423,
                $"Your account has been suspended. Reason: {punishment.Reason}. Expired at: {punishment.ExpiredAt?.ToString() ?? "never"}"
            );

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        request.DeviceName ??= userAgent;

        // Trying to pick up challenges from the same IP address and user agent
        var existingChallenge = await db.AuthChallenges
            .Where(e => e.AccountId == account.Id)
            .Where(e => e.IpAddress == ipAddress)
            .Where(e => e.UserAgent == userAgent)
            .Where(e => e.StepRemain > 0)
            .Where(e => e.ExpiredAt != null && now < e.ExpiredAt)
            .Where(e => e.DeviceId == request.DeviceId)
            .FirstOrDefaultAsync();
        if (existingChallenge is not null) return existingChallenge;

        var challenge = new SnAuthChallenge
        {
            ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddHours(1)),
            StepTotal = await auth.DetectChallengeRisk(Request, account),
            Audiences = request.Audiences,
            Scopes = request.Scopes,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Location = geo.GetPointFromIp(ipAddress),
            DeviceId = request.DeviceId,
            DeviceName = request.DeviceName,
            Platform = request.Platform,
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
    public async Task<ActionResult<SnAuthChallenge>> GetChallenge([FromRoute] Guid id)
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .ThenInclude(e => e.Profile)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (challenge is not null) return challenge;
        logger.LogWarning("GetChallenge: challenge not found (challengeId={ChallengeId}, ip={IpAddress})",
            id, HttpContext.Connection.RemoteIpAddress?.ToString());
        return NotFound("Auth challenge was not found.");

    }

    [HttpGet("challenge/{id:guid}/factors")]
    public async Task<ActionResult<List<SnAccountAuthFactor>>> GetChallengeFactors([FromRoute] Guid id)
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
        [FromRoute] Guid factorId
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
            await accounts.SendFactorCode(challenge.Account, factor);
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
    public async Task<ActionResult<SnAuthChallenge>> DoChallenge(
        [FromRoute] Guid id,
        [FromBody] PerformChallengeRequest request
    )
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (challenge is null) return NotFound("Auth challenge was not found.");

        var factor = await db.AccountAuthFactors
            .Where(f => f.Id == request.FactorId)
            .Where(f => f.AccountId == challenge.AccountId)
            .FirstOrDefaultAsync();
        if (factor is null) return NotFound("Auth factor was not found.");
        if (factor.EnabledAt is null) return BadRequest("Auth factor is not enabled.");
        if (factor.Trustworthy <= 0) return BadRequest("Auth factor is not trustworthy.");

        if (challenge.StepRemain == 0) return challenge;
        var now = SystemClock.Instance.GetCurrentInstant();
        if (challenge.ExpiredAt.HasValue && now > challenge.ExpiredAt.Value)
            return BadRequest();

        // prevent reusing the same factor in one challenge
        if (challenge.BlacklistFactors.Contains(factor.Id))
            return BadRequest("Auth factor already used.");

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
        catch (Exception)
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

            logger.LogWarning(
                "DoChallenge: authentication failure (challengeId={ChallengeId}, factorId={FactorId}, accountId={AccountId}, failedAttempts={FailedAttempts}, factorType={FactorType}, ip={IpAddress}, uaLength={UaLength})",
                challenge.Id, factor.Id, challenge.AccountId, challenge.FailedAttempts, factor.Type,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers.UserAgent.ToString().Length);

            return BadRequest("Invalid password.");
        }

        if (challenge.StepRemain == 0)
        {
            AccountService.SetCultureInfo(challenge.Account);
            await pusher.SendPushNotificationToUserAsync(new SendPushNotificationToUserRequest
            {
                Notification = new PushNotification
                {
                    Topic = "auth.login",
                    Title = localizer.Get("newLoginTitle"),
                    Body = localizer.Get("newLoginBody", args: new { deviceName = challenge.DeviceName ?? "unknown", ipAddress = challenge.IpAddress ?? "unknown" }),
                    IsSavable = true
                },
                UserId = challenge.AccountId.ToString()
            });
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

    public class NewSessionRequest
    {
        [Required] [MaxLength(512)] public string DeviceId { get; set; } = null!;
        [MaxLength(1024)] public string? DeviceName { get; set; }
        [Required] public Shared.Models.ClientPlatform Platform { get; set; }
        public Instant? ExpiredAt { get; set; }
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
                try
                {
                    var tk = await auth.CreateSessionAndIssueToken(challenge);
                    return Ok(new TokenExchangeResponse { Token = tk });
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(ex.Message);
                }
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

    [HttpPost("login/session")]
    [Microsoft.AspNetCore.Authorization.Authorize] // Use full namespace to avoid ambiguity with DysonNetwork.Pass.Permission.Authorize
    public async Task<ActionResult<TokenExchangeResponse>> LoginFromSession([FromBody] NewSessionRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized();

        var newSession = await auth.CreateSessionFromParentAsync(
            currentSession,
            request.DeviceId,
            request.DeviceName,
            request.Platform,
            request.ExpiredAt
        );

        var tk = auth.CreateToken(newSession);

        // Set cookie using HttpContext, similar to CreateSessionAndIssueToken
        HttpContext.Response.Cookies.Append(AuthConstants.CookieTokenName, tk, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Domain = _cookieDomain,
            Expires = request.ExpiredAt?.ToDateTimeOffset() ?? DateTime.UtcNow.AddYears(20)
        });

        return Ok(new TokenExchangeResponse { Token = tk });
    }
}