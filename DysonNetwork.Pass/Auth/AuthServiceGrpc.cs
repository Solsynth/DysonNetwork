using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Pass.Auth;

public class AuthServiceGrpc(
    TokenAuthService token,
    AuthService auth
)
    : Shared.Proto.AuthService.AuthServiceBase
{
    public override async Task<AuthenticateResponse> Authenticate(
        AuthenticateRequest request,
        ServerCallContext context
    )
    {
        var (valid, session, message) = await token.AuthenticateTokenAsync(request.Token, request.IpAddress);
        if (!valid || session is null)
            return new AuthenticateResponse { Valid = false, Message = message ?? "Authentication failed." };

        return new AuthenticateResponse { Valid = true, Session = session.ToProtoValue() };
    }

    public override async Task<ValidateResponse> ValidatePin(ValidatePinRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var valid = await auth.ValidatePinCode(accountId, request.Pin);
        return new ValidateResponse { Valid = valid };
    }
    
    public override async Task<ValidateResponse> ValidateCaptcha(ValidateCaptchaRequest request, ServerCallContext context)
    {
        var valid = await auth.ValidateCaptcha(request.Token);
        return new ValidateResponse { Valid = valid };
    }
}