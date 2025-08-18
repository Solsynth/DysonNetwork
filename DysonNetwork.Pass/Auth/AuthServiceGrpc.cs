using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Auth;

public class AuthServiceGrpc(
    AuthService authService,
    SubscriptionService subscriptions,
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
        var (valid, session, message) = await authService.AuthenticateTokenAsync(request.Token);
        if (!valid || session is null)
            return new AuthenticateResponse { Valid = false, Message = message ?? "Authentication failed." };

        return new AuthenticateResponse { Valid = true, Session = session.ToProtoValue() };
    }

    public override async Task<ValidateResponse> ValidatePin(ValidatePinRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var valid = await authService.ValidatePinCode(accountId, request.Pin);
        return new ValidateResponse { Valid = valid };
    }
    
    public override async Task<ValidateResponse> ValidateCaptcha(ValidateCaptchaRequest request, ServerCallContext context)
    {
        var valid = await authService.ValidateCaptcha(request.Token);
        return new ValidateResponse { Valid = valid };
    }
}