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
        public List<string> Claims { get; set; } = new();
        public List<string> Audiences { get; set; } = new();
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
            Claims = request.Claims,
            Audiences = request.Audiences,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceId = request.DeviceId,
        }.Normalize();

        await db.AuthChallenges.AddAsync(challenge);
        await db.SaveChangesAsync();
        return challenge;
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
        if (challenge is null) return new NotFoundResult();

        var factor = await db.AccountAuthFactors.FindAsync(request.FactorId);
        if (factor is null) return new NotFoundResult();

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
    
    [HttpPost("challenge/{id}/grant")]
    public async Task<ActionResult<SignedTokenPair>> GrantChallengeToken([FromRoute] Guid id)
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (challenge is null) return new NotFoundResult();
        if (challenge.StepRemain != 0) return new BadRequestResult();
        
        var session = await db.AuthSessions
            .Where(e => e.Challenge == challenge)
            .FirstOrDefaultAsync();
        if (session is not null) return new BadRequestResult();

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
    }
    
    public class TokenExchangeRequest
    {
        public string GrantType { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }
    
    [HttpPost("token")]
    public async Task<ActionResult<SignedTokenPair>> ExchangeToken([FromBody] TokenExchangeRequest request)
    {
        switch (request.GrantType)
        {
            case "refresh_token":
                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(request.RefreshToken);
                var sessionIdClaim = token.Claims.FirstOrDefault(c => c.Type == "session_id")?.Value;

                if (!Guid.TryParse(sessionIdClaim, out var sessionId))
                    return new UnauthorizedResult();

                var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
                if (session is null) return new NotFoundResult();
                
                session.LastGrantedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
                await db.SaveChangesAsync();

                return auth.CreateToken(session);
            default:
                return new BadRequestResult();
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