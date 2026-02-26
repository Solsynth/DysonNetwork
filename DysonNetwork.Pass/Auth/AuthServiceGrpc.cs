using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Pass.Auth;

public class AuthServiceGrpc(
    TokenAuthService token,
    AuthService auth
)
    : DyAuthService.DyAuthServiceBase
{
    public override async Task<DyAuthenticateResponse> Authenticate(
        DyAuthenticateRequest request,
        ServerCallContext context
    )
    {
        var (valid, session, message) = await token.AuthenticateTokenAsync(request.Token, request.IpAddress);
        if (!valid || session is null)
            return new DyAuthenticateResponse { Valid = false, Message = message ?? "Authentication failed." };

        return new DyAuthenticateResponse { Valid = true, Session = session.ToProtoValue() };
    }

    public override async Task<DyValidateResponse> ValidatePin(DyValidatePinRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var valid = await auth.ValidatePinCode(accountId, request.Pin);
        return new DyValidateResponse { Valid = valid };
    }
    
    public override async Task<DyValidateResponse> ValidateCaptcha(DyValidateCaptchaRequest request, ServerCallContext context)
    {
        var valid = await auth.ValidateCaptcha(request.Token);
        return new DyValidateResponse { Valid = valid };
    }
}