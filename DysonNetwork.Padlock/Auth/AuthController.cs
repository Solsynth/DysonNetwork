using System.ComponentModel.DataAnnotations;
using DysonNetwork.Padlock.Account;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[Route("/api/auth")]
public class AuthController(
    AppDatabase db,
    AccountService accounts,
    AuthService auth,
    GeoService geo,
    DyRingService.DyRingServiceClient pusher,
    IConfiguration configuration,
    ILocalizationService localizer,
    ILogger<AuthController> logger
) : ControllerBase
{
    private readonly string? _cookieDomain = configuration["AuthToken:CookieDomain"];

    private CookieOptions CreateCookieOptions(Instant expiresAt)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt.ToDateTimeOffset()
        };
        if (!string.IsNullOrEmpty(_cookieDomain))
            options.Domain = _cookieDomain;
        return options;
    }

    private void SetAuthCookies(string accessToken, Instant accessExpiresAt, string refreshToken, Instant refreshExpiresAt)
    {
        Response.Cookies.Append(AuthConstants.CookieTokenName, accessToken, CreateCookieOptions(accessExpiresAt));
        Response.Cookies.Append(AuthConstants.RefreshCookieTokenName, refreshToken, CreateCookieOptions(refreshExpiresAt));
    }

    private void ClearAuthCookies()
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax
        };
        if (!string.IsNullOrEmpty(_cookieDomain))
            options.Domain = _cookieDomain;
        Response.Cookies.Delete(AuthConstants.CookieTokenName, options);
        Response.Cookies.Delete(AuthConstants.RefreshCookieTokenName, options);
    }

    public class ChallengeRequest
    {
        [Required] public ClientPlatform Platform { get; set; }
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

        var punishment = await accounts.GetActivePunishmentOverview(account.Id);
        if (punishment is { Type: PunishmentType.DisableAccount or PunishmentType.BlockLogin })
            return StatusCode(423, new ApiError
            {
                Code = "ACCOUNT_LOCKED",
                Message = "Account is locked due to a punishment.",
                Detail = punishment.Reason,
                Status = 423,
                TraceId = HttpContext.TraceIdentifier
            });

        var now = SystemClock.Instance.GetCurrentInstant();
        var ipAddress = HttpContext.GetClientIpAddress();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        request.DeviceName ??= userAgent;

        var existingChallenge = await db.AuthChallenges
            .Where(e => e.AccountId == account.Id)
            .Where(e => e.IpAddress == ipAddress)
            .Where(e => e.UserAgent == userAgent)
            .Where(e => e.StepRemain > 0)
            .Where(e => e.ExpiredAt != null && now < e.ExpiredAt)
            .Where(e => e.DeviceId == request.DeviceId)
            .OrderByDescending(e => e.CreatedAt)
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

        return challenge;
    }

    [HttpGet("challenge/{id:guid}")]
    public async Task<ActionResult<SnAuthChallenge>> GetChallenge([FromRoute] Guid id)
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (challenge is not null) return challenge;
        logger.LogWarning("GetChallenge: challenge not found (challengeId={ChallengeId}, ip={IpAddress})",
            id, HttpContext.GetClientIpAddress());
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
            : challenge.Account.AuthFactors
                .Where(e => e is { EnabledAt: not null, Trustworthy: >= 1 } && e.Type != AccountAuthFactorType.RecoveryCode)
                .ToList();
    }

    [HttpPost("challenge/{id:guid}/factors/{factorId:guid}")]
    public async Task<ActionResult> RequestFactorCode([FromRoute] Guid id, [FromRoute] Guid factorId)
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .Where(e => e.Id == id).FirstOrDefaultAsync();
        if (challenge is null) return NotFound("Auth challenge was not found.");

        var factor = await db.AccountAuthFactors
            .Where(e => e.Id == factorId)
            .Where(e => e.AccountId == challenge.AccountId).FirstOrDefaultAsync();
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
    public async Task<ActionResult<SnAuthChallenge>> DoChallenge([FromRoute] Guid id, [FromBody] PerformChallengeRequest request)
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
            await db.SaveChangesAsync();

            logger.LogWarning(
                "DoChallenge: authentication failure (challengeId={ChallengeId}, factorId={FactorId}, accountId={AccountId}, failedAttempts={FailedAttempts}, factorType={FactorType}, ip={IpAddress}, uaLength={UaLength})",
                challenge.Id, factor.Id, challenge.AccountId, challenge.FailedAttempts, factor.Type,
                HttpContext.GetClientIpAddress(),
                HttpContext.Request.Headers.UserAgent.ToString().Length);

            return BadRequest("Invalid password.");
        }

        if (challenge.StepRemain == 0)
        {
            await pusher.SendPushNotificationToUserAsync(new DySendPushNotificationToUserRequest
            {
                Notification = new DyPushNotification
                {
                    Topic = "auth.login",
                    Title = localizer.Get("newLoginTitle", challenge.Account.Language),
                    Body = localizer.Get("newLoginBody", locale: challenge.Account.Language, args: new
                        { deviceName = challenge.DeviceName ?? "unknown", ipAddress = challenge.IpAddress ?? "unknown" }),
                    IsSavable = true
                },
                UserId = challenge.AccountId.ToString()
            });
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

    public class NewSessionRequest
    {
        [Required] [MaxLength(512)] public string DeviceId { get; set; } = null!;
        [MaxLength(1024)] public string? DeviceName { get; set; }
        [Required] public ClientPlatform Platform { get; set; }
        public Instant? ExpiredAt { get; set; }
    }

    [HttpPost("token")]
    public async Task<ActionResult<TokenExchangeResponse>> ExchangeToken([FromBody] TokenExchangeRequest request)
    {
        switch (request.GrantType)
        {
            case "authorization_code":
                var challengeId = Guid.TryParse(request.Code, out var parsedChallengeId) ? parsedChallengeId : Guid.Empty;
                if (challengeId == Guid.Empty)
                    return BadRequest("Invalid or missing authorization code.");

                var challenge = await db.AuthChallenges
                    .Include(e => e.Account)
                    .Where(e => e.Id == challengeId)
                    .FirstOrDefaultAsync();
                if (challenge is null)
                    return BadRequest("Authorization code not found or expired.");

                var punishment = await accounts.GetActivePunishmentOverview(challenge.AccountId);
                if (punishment is { Type: PunishmentType.DisableAccount or PunishmentType.BlockLogin })
                    return StatusCode(423, new ApiError
                    {
                        Code = "ACCOUNT_LOCKED",
                        Message = "Account is locked due to a punishment.",
                        Detail = punishment.Reason,
                        Status = 423,
                        TraceId = HttpContext.TraceIdentifier
                    });

                try
                {
                    var pair = await auth.CreateSessionAndIssueTokens(challenge);
                    SetAuthCookies(pair.AccessToken, pair.AccessTokenExpiresAt, pair.RefreshToken, pair.RefreshTokenExpiresAt);

                    var now = SystemClock.Instance.GetCurrentInstant();
                    return Ok(new TokenExchangeResponse
                    {
                        Token = pair.AccessToken,
                        RefreshToken = pair.RefreshToken,
                        ExpiresIn = (long)Math.Max(0, (pair.AccessTokenExpiresAt - now).TotalSeconds),
                        RefreshExpiresIn = (long)Math.Max(0, (pair.RefreshTokenExpiresAt - now).TotalSeconds)
                    });
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(ex.Message);
                }
            case "refresh_token":
                var submittedRefresh = request.RefreshToken;
                if (string.IsNullOrWhiteSpace(submittedRefresh))
                    Request.Cookies.TryGetValue(AuthConstants.RefreshCookieTokenName, out submittedRefresh);
                if (string.IsNullOrWhiteSpace(submittedRefresh))
                    return BadRequest("Missing refresh token.");

                try
                {
                    var pair = await auth.RefreshSessionAndIssueTokens(submittedRefresh);
                    SetAuthCookies(pair.AccessToken, pair.AccessTokenExpiresAt, pair.RefreshToken, pair.RefreshTokenExpiresAt);

                    var now = SystemClock.Instance.GetCurrentInstant();
                    return Ok(new TokenExchangeResponse
                    {
                        Token = pair.AccessToken,
                        RefreshToken = pair.RefreshToken,
                        ExpiresIn = (long)Math.Max(0, (pair.AccessTokenExpiresAt - now).TotalSeconds),
                        RefreshExpiresIn = (long)Math.Max(0, (pair.RefreshTokenExpiresAt - now).TotalSeconds)
                    });
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(ex.Message);
                }
            default:
                return BadRequest("Unsupported grant type.");
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<TokenExchangeResponse>> RefreshToken()
    {
        if (!Request.Cookies.TryGetValue(AuthConstants.RefreshCookieTokenName, out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
            return BadRequest("Missing refresh token.");

        try
        {
            var pair = await auth.RefreshSessionAndIssueTokens(refreshToken);
            SetAuthCookies(pair.AccessToken, pair.AccessTokenExpiresAt, pair.RefreshToken, pair.RefreshTokenExpiresAt);

            var now = SystemClock.Instance.GetCurrentInstant();
            return Ok(new TokenExchangeResponse
            {
                Token = pair.AccessToken,
                RefreshToken = pair.RefreshToken,
                ExpiresIn = (long)Math.Max(0, (pair.AccessTokenExpiresAt - now).TotalSeconds),
                RefreshExpiresIn = (long)Math.Max(0, (pair.RefreshTokenExpiresAt - now).TotalSeconds)
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("captcha")]
    public async Task<ActionResult> ValidateCaptcha([FromBody] string token)
    {
        var result = await auth.ValidateCaptcha(token);
        return result ? Ok() : BadRequest();
    }

    public class RecoveryRequest
    {
        [Required] public string Account { get; set; } = null!;
        [Required] public string RecoveryCode { get; set; } = null!;
        [Required] public string CaptchaToken { get; set; } = null!;
        [Required] public string DeviceId { get; set; } = null!;
        [MaxLength(1024)] public string? DeviceName { get; set; }
        public ClientPlatform Platform { get; set; } = ClientPlatform.Unidentified;
    }

    [HttpPost("recover")]
    public async Task<ActionResult<TokenExchangeResponse>> RecoverAccount([FromBody] RecoveryRequest request)
    {
        var captchaResp = await auth.ValidateCaptcha(request.CaptchaToken);
        if (!captchaResp)
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.CaptchaToken)] = ["Invalid captcha token."]
            }, traceId: HttpContext.TraceIdentifier));

        var account = await accounts.LookupAccount(request.Account);
        if (account is null)
            return BadRequest(new ApiError
            {
                Code = "NOT_FOUND",
                Message = "Unable to find the account.",
                Detail = request.Account,
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });

        try
        {
            var pair = await auth.RecoverAccountWithRecoveryCodeAsync(
                account,
                request.RecoveryCode,
                request.DeviceId,
                request.Platform,
                request.DeviceName
            );

            SetAuthCookies(pair.AccessToken, pair.AccessTokenExpiresAt, pair.RefreshToken, pair.RefreshTokenExpiresAt);

            var now = SystemClock.Instance.GetCurrentInstant();
            return Ok(new TokenExchangeResponse
            {
                Token = pair.AccessToken,
                RefreshToken = pair.RefreshToken,
                ExpiresIn = (long)Math.Max(0, (pair.AccessTokenExpiresAt - now).TotalSeconds),
                RefreshExpiresIn = (long)Math.Max(0, (pair.RefreshTokenExpiresAt - now).TotalSeconds)
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError
            {
                Code = "RECOVERY_FAILED",
                Message = ex.Message,
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [RequireInteractiveSession]
    public async Task<IActionResult> Logout()
    {
        if (HttpContext.Items["CurrentSession"] is SnAuthSession currentSession)
            await auth.RevokeSessionAsync(currentSession.Id);

        ClearAuthCookies();
        return Ok();
    }

    [HttpPost("login/session")]
    [Authorize]
    [RequireInteractiveSession]
    public async Task<ActionResult<TokenExchangeResponse>> LoginFromSession([FromBody] NewSessionRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized();

        try
        {
            var newSession = await auth.CreateSessionFromParentAsync(
                currentSession,
                request.DeviceId,
                request.DeviceName,
                request.Platform,
                request.ExpiredAt
            );

            var pair = await auth.CreateTokenPair(newSession);
            SetAuthCookies(pair.AccessToken, pair.AccessTokenExpiresAt, pair.RefreshToken, pair.RefreshTokenExpiresAt);

            var now = SystemClock.Instance.GetCurrentInstant();
            return Ok(new TokenExchangeResponse
            {
                Token = pair.AccessToken,
                RefreshToken = pair.RefreshToken,
                ExpiresIn = (long)Math.Max(0, (pair.AccessTokenExpiresAt - now).TotalSeconds),
                RefreshExpiresIn = (long)Math.Max(0, (pair.RefreshTokenExpiresAt - now).TotalSeconds)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // Auth-only identity endpoint. Profile-rich identity remains in Pass.
    [HttpGet("me")]
    [Authorize]
    public ActionResult<object> GetCurrentAuthIdentity()
    {
        var user = HttpContext.Items["CurrentUser"] as SnAccount;
        var session = HttpContext.Items["CurrentSession"] as SnAuthSession;
        if (user is null || session is null) return Unauthorized();

        return Ok(new
        {
            user.Id,
            user.Name,
            user.Nick,
            user.Language,
            user.Region,
            user.IsSuperuser,
            user.ActivatedAt,
            session_id = session.Id,
            token_type = HttpContext.Items["CurrentTokenType"]?.ToString()
        });
    }

    [HttpPost("sudo")]
    [Authorize]
    [RequireInteractiveSession]
    public async Task<IActionResult> EnableSudoMode([FromBody] SudoRequest request)
    {
        var session = HttpContext.Items["CurrentSession"] as SnAuthSession;
        if (session is null) return Unauthorized();

        var valid = await auth.ValidateSudoMode(session, request.PinCode);
        if (!valid) return BadRequest(new { error = "Invalid PIN code" });

        return Ok();
    }
}

public record SudoRequest(string? PinCode);

public class TokenExchangeResponse
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public long ExpiresIn { get; set; }
    public long RefreshExpiresIn { get; set; }
}
