using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Auth;

public class AuthServiceGrpc(AuthService authService, AppDatabase db) : Shared.Proto.AuthService
{
    public async Task<Shared.Proto.AuthSession> Authenticate(AuthenticateRequest request, ServerCallContext context)
    {
        if (!authService.ValidateToken(request.Token, out var sessionId))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid token."));
        }

        var session = await db.AuthSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Session not found."));
        }

        return session.ToProtoValue();
    }
}
