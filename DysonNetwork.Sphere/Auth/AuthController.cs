using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;

namespace DysonNetwork.Sphere.Auth;

[ApiController]
[Route("/auth")]
public class AuthController(AppDatabase db, AccountService accounts, AuthService auth, IHttpContextAccessor httpContext)
{
    public class ChallengeRequest
    {
        [Required] [MaxLength(256)] public string Account { get; set; } = string.Empty;
        [MaxLength(512)] public string? DeviceId { get; set; }
        public List<string> Audiences { get; set; } = new();
        public List<string> Scopes { get; set; } = new();
    }

    [HttpPost("challenge")]
    public async Task<ActionResult<Challenge>> StartChallenge([FromBody] ChallengeRequest request)
    {
        var account = await accounts.LookupAccount(request.Account);
        if (account is null) return new NotFoundResult();

        var ipAddress = httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.HttpContext?.Request.Headers.UserAgent.ToString();

        var challenge = new Challenge
        {
            Account = account,
            ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddHours(1)),
            StepTotal = 1,
            Audiences = request.Audiences,
            Scopes = request.Scopes,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceId = request.DeviceId,
        }.Normalize();

        await db.AuthChallenges.AddAsync(challenge);
        await db.SaveChangesAsync();
        return challenge;
    }

    [HttpGet("challenge/{id}/factors")]
    public async Task<ActionResult<List<AccountAuthFactor>>> GetChallengeFactors([FromRoute] Guid id)
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .Include(e => e.Account.AuthFactors)
            .Where(e => e.Id == id).FirstOrDefaultAsync();
        return challenge is null
            ? new NotFoundObjectResult("Auth challenge was not found.")
            : challenge.Account.AuthFactors.ToList();
    }

    public class PerformChallengeRequest
    {
        [Required] public long FactorId { get; set; }
        [Required] public string Password { get; set; } = string.Empty;
    }

    [HttpPatch("challenge/{id}")]
    public async Task<ActionResult<Challenge>> DoChallenge(
        [FromRoute] Guid id,
        [FromBody] PerformChallengeRequest request
    )
    {
        var challenge = await db.AuthChallenges.FindAsync(id);
        if (challenge is null) return new NotFoundObjectResult("Auth challenge was not found.");

        var factor = await db.AccountAuthFactors.FindAsync(request.FactorId);
        if (factor is null) return new NotFoundObjectResult("Auth factor was not found.");

        if (challenge.StepRemain == 0) return challenge;
        if (challenge.ExpiredAt.HasValue && challenge.ExpiredAt.Value < Instant.FromDateTimeUtc(DateTime.UtcNow))
            return new BadRequestResult();

        try
        {
            if (factor.VerifyPassword(request.Password))
            {
                challenge.StepRemain--;
                challenge.BlacklistFactors.Add(factor.Id);
            }
        }
        catch
        {
            return new BadRequestResult();
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
                    return new BadRequestObjectResult("Invalid or missing authorization code.");
                var challenge = await db.AuthChallenges
                    .Include(e => e.Account)
                    .Where(e => e.Id == code)
                    .FirstOrDefaultAsync();
                if (challenge is null)
                    return new NotFoundObjectResult("Authorization code not found or expired.");
                if (challenge.StepRemain != 0)
                    return new BadRequestObjectResult("Challenge not yet completed.");

                session = await db.AuthSessions
                    .Where(e => e.Challenge == challenge)
                    .FirstOrDefaultAsync();
                if (session is not null)
                    return new BadRequestObjectResult("Session already exists for this challenge.");

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
                    return new UnauthorizedObjectResult("Invalid or missing session_id claim in refresh token.");

                session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
                if (session is null)
                    return new NotFoundObjectResult("Session not found or expired.");

                session.LastGrantedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
                await db.SaveChangesAsync();

                return auth.CreateToken(session);
            default:
                return new BadRequestObjectResult("Unsupported grant type.");
        }
    }

    [Authorize]
    [HttpGet("test")]
    public async Task<ActionResult> Test()
    {
        var sessionIdClaim = httpContext.HttpContext?.User.FindFirst("session_id")?.Value;
        if (!Guid.TryParse(sessionIdClaim, out var sessionId))
            return new UnauthorizedResult();

        var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null) return new NotFoundResult();

        return new OkObjectResult(session);
    }
}