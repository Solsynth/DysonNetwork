using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Auth;

public class AuthServiceGrpc(
    AuthService authService,
    ICacheService cache,
    AppDatabase db
)
    : Shared.Proto.AuthService.AuthServiceBase
{
    public override async Task<AuthenticateResponse> Authenticate(
        AuthenticateRequest request,
        ServerCallContext context
    )
    {
        if (!authService.ValidateToken(request.Token, out var sessionId))
            return new AuthenticateResponse { Valid = false, Message = "Invalid token." };
        
        var session = await cache.GetAsync<AuthSession>($"{DysonTokenAuthHandler.AuthCachePrefix}{sessionId}");
        if (session is not null)
            return new AuthenticateResponse { Valid = true, Session = session.ToProtoValue() };

        session = await db.AuthSessions
            .AsNoTracking()
            .Include(e => e.Challenge)
            .Include(e => e.Account)
            .ThenInclude(e => e.Profile)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null)
            return new AuthenticateResponse { Valid = false, Message = "Session was not found." };
        var now = SystemClock.Instance.GetCurrentInstant();
        if (session.ExpiredAt.HasValue && session.ExpiredAt < now)
            return new AuthenticateResponse { Valid = false, Message = "Session has been expired." };
        
        await cache.SetWithGroupsAsync(
            $"auth:{sessionId}",
            session,
            [$"{Account.AccountService.AccountCachePrefix}{session.Account.Id}"],
            TimeSpan.FromHours(1)
        );

        return new AuthenticateResponse { Valid = true, Session = session.ToProtoValue() };
    }
}