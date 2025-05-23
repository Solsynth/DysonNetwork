using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using DysonNetwork.Sphere.Connection;

namespace DysonNetwork.Sphere.Auth;

[ApiController]
[Route("/auth")]
public class AuthController(
    AppDatabase db,
    AccountService accounts,
    AuthService auth,
    GeoIpService geo,
    ActionLogService als
) : ControllerBase
{
    public class ChallengeRequest
    {
        [Required] public ChallengePlatform Platform { get; set; }
        [Required] [MaxLength(256)] public string Account { get; set; } = null!;
        [Required] [MaxLength(512)] public string DeviceId { get; set; } = null!;
        public List<string> Audiences { get; set; } = new();
        public List<string> Scopes { get; set; } = new();
    }

    [HttpPost("challenge")]
    public async Task<ActionResult<Challenge>> StartChallenge([FromBody] ChallengeRequest request)
    {
        var account = await accounts.LookupAccount(request.Account);
        if (account is null) return NotFound("Account was not found.");

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        // Trying to pick up challenges from the same IP address and user agent
        var existingChallenge = await db.AuthChallenges
            .Where(e => e.Account == account)
            .Where(e => e.IpAddress == ipAddress)
            .Where(e => e.UserAgent == userAgent)
            .Where(e => e.StepRemain > 0)
            .Where(e => e.ExpiredAt != null && now < e.ExpiredAt)
            .FirstOrDefaultAsync();
        if (existingChallenge is not null) return existingChallenge;

        var challenge = new Challenge
        {
            ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddHours(1)),
            StepTotal = 1,
            Platform = request.Platform,
            Audiences = request.Audiences,
            Scopes = request.Scopes,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Location = geo.GetPointFromIp(ipAddress),
            DeviceId = request.DeviceId,
            AccountId = account.Id
        }.Normalize();

        await db.AuthChallenges.AddAsync(challenge);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(ActionLogType.ChallengeAttempt,
            new Dictionary<string, object> { { "challenge_id", challenge.Id } }, Request, account
        );

        return challenge;
    }

    [HttpGet("challenge/{id:guid}/factors")]
    public async Task<ActionResult<List<AccountAuthFactor>>> GetChallengeFactors([FromRoute] Guid id)
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .Include(e => e.Account.AuthFactors)
            .Where(e => e.Id == id).FirstOrDefaultAsync();
        return challenge is null
            ? NotFound("Auth challenge was not found.")
            : challenge.Account.AuthFactors.ToList();
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
            .Where(e => e.Account == challenge.Account).FirstOrDefaultAsync();
        if (factor is null) return NotFound("Auth factor was not found.");

        // TODO do the logic here

        return Ok();
    }

    public class PerformChallengeRequest
    {
        [Required] public Guid FactorId { get; set; }
        [Required] public string Password { get; set; } = string.Empty;
    }

    [HttpPatch("challenge/{id:guid}")]
    public async Task<ActionResult<Challenge>> DoChallenge(
        [FromRoute] Guid id,
        [FromBody] PerformChallengeRequest request
    )
    {
        var challenge = await db.AuthChallenges.Include(e => e.Account).FirstOrDefaultAsync(e => e.Id == id);
        if (challenge is null) return NotFound("Auth challenge was not found.");

        var factor = await db.AccountAuthFactors.FindAsync(request.FactorId);
        if (factor is null) return NotFound("Auth factor was not found.");

        if (challenge.StepRemain == 0) return challenge;
        if (challenge.ExpiredAt.HasValue && challenge.ExpiredAt.Value < Instant.FromDateTimeUtc(DateTime.UtcNow))
            return BadRequest();

        try
        {
            if (factor.VerifyPassword(request.Password))
            {
                challenge.StepRemain--;
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

    [HttpPost("token")]
    public async Task<ActionResult<SignedTokenPair>> ExchangeToken([FromBody] TokenExchangeRequest request)
    {
        Session? session;
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

                session = await db.AuthSessions
                    .Where(e => e.Challenge == challenge)
                    .FirstOrDefaultAsync();
                if (session is not null)
                    return BadRequest("Session already exists for this challenge.");

                session = new Session
                {
                    LastGrantedAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
                    ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(30)),
                    Account = challenge.Account,
                    Challenge = challenge,
                };

                db.AuthSessions.Add(session);
                await db.SaveChangesAsync();

                return auth.CreateToken(session);
            case "refresh_token":
                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(request.RefreshToken);
                var sessionIdClaim = token.Claims.FirstOrDefault(c => c.Type == "session_id")?.Value;

                if (!Guid.TryParse(sessionIdClaim, out var sessionId))
                    return Unauthorized("Invalid or missing session_id claim in refresh token.");

                session = await db.AuthSessions
                    .Include(e => e.Account)
                    .Include(e => e.Challenge)
                    .FirstOrDefaultAsync(s => s.Id == sessionId);
                if (session is null)
                    return NotFound("Session not found or expired.");

                session.LastGrantedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
                await db.SaveChangesAsync();

                return auth.CreateToken(session);
            default:
                return BadRequest("Unsupported grant type.");
        }
    }

    [HttpPost("captcha")]
    public async Task<ActionResult> ValidateCaptcha([FromBody] string token)
    {
        var result = await auth.ValidateCaptcha(token);
        return result ? Ok() : BadRequest();
    }
}