using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DysonNetwork.Shared.Tests.Auth;

/// <summary>
/// The step-up gate is answered by a live auth-service call, so these tests drive the real
/// middleware over a <see cref="DefaultHttpContext"/> with a stubbed gRPC transport. The repo
/// has no request-level test host for this middleware (the services wire it after
/// <c>UseAuthentication</c>), so the endpoint metadata, the auth items and the gRPC answers are
/// supplied directly.
/// </summary>
public class RemotePermissionMiddlewareSudoTests
{
    private const string SessionId = "6f2a1b4c-8d3e-4f50-9a61-7b8c9d0e1f20";
    private const string AccountId = "0d1e2f3a-4b5c-6d7e-8f90-1a2b3c4d5e6f";

    [Fact]
    public async Task SudoOnlyEndpoint_RequiresElevationAndCarriesTheFactorHint()
    {
        var invoker = new FakeCallInvoker
        {
            ValidateSudo = _ =>
            {
                var response = new DyValidateSudoResponse { Valid = false };
                response.FactorTypes.Add("totp");
                response.FactorTypes.Add("password");
                return response;
            }
        };
        var context = CreateContext(permissions: [], requireSudo: true, session: LoginSession());

        var run = await RunAsync(context, invoker);

        Assert.Equal(StatusCodes.Status403Forbidden, run.Status);
        Assert.False(run.NextCalled);
        Assert.Equal(1, invoker.ValidateSudoCalls);
        Assert.Equal(SessionId, invoker.LastValidateSudoRequest!.SessionId);
        Assert.True(invoker.LastValidateSudoOptions!.Value.CancellationToken.CanBeCanceled);

        Assert.Equal("AUTH_SUDO_REQUIRED", run.Body!.Value.GetProperty("code").GetString());
        Assert.Equal("This action requires re-authentication.", run.Body.Value.GetProperty("message").GetString());
        Assert.Equal(403, run.Body.Value.GetProperty("status").GetInt32());
        Assert.Equal(
            "totp,password",
            run.Body.Value.GetProperty("meta").GetProperty("factor_types").GetString()
        );
    }

    [Fact]
    public async Task SudoDenial_OmitsTheHintWhenTheAuthServiceReportsNone()
    {
        var invoker = new FakeCallInvoker { ValidateSudo = _ => new DyValidateSudoResponse { Valid = false } };
        var context = CreateContext(permissions: [], requireSudo: true, session: LoginSession());

        var run = await RunAsync(context, invoker);

        Assert.Equal(StatusCodes.Status403Forbidden, run.Status);
        Assert.Equal("AUTH_SUDO_REQUIRED", run.Body!.Value.GetProperty("code").GetString());
        Assert.False(run.Body.Value.TryGetProperty("meta", out _));
    }

    [Fact]
    public async Task ElevatedSession_ProceedsToTheEndpoint()
    {
        var invoker = new FakeCallInvoker { ValidateSudo = _ => new DyValidateSudoResponse { Valid = true } };
        var context = CreateContext(permissions: [], requireSudo: true, session: LoginSession());

        var run = await RunAsync(context, invoker);

        Assert.True(run.NextCalled);
        Assert.Equal(1, invoker.ValidateSudoCalls);
        Assert.Equal(StatusCodes.Status200OK, run.Status);
    }

    [Fact]
    public async Task Superuser_StillHasToElevate()
    {
        var invoker = new FakeCallInvoker { ValidateSudo = _ => new DyValidateSudoResponse { Valid = false } };
        var context = CreateContext(
            permissions: [],
            requireSudo: true,
            session: LoginSession(),
            superuser: true
        );

        var run = await RunAsync(context, invoker);

        Assert.Equal(StatusCodes.Status403Forbidden, run.Status);
        Assert.False(run.NextCalled);
        Assert.Equal("AUTH_SUDO_REQUIRED", run.Body!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ApiKeySession_IsDeniedWithoutAskingTheAuthService()
    {
        var invoker = new FakeCallInvoker { ValidateSudo = _ => new DyValidateSudoResponse { Valid = true } };
        var session = LoginSession();
        session.Type = DySessionType.DyApiKey;
        var context = CreateContext(permissions: [], requireSudo: true, session: session);

        var run = await RunAsync(context, invoker);

        Assert.Equal(StatusCodes.Status403Forbidden, run.Status);
        Assert.False(run.NextCalled);
        Assert.Equal(0, invoker.ValidateSudoCalls);
        Assert.Equal("AUTH_SUDO_REQUIRED", run.Body!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FailingValidation_FailsClosed()
    {
        var invoker = new FakeCallInvoker
        {
            ValidateSudoError = new RpcException(new Status(StatusCode.Unavailable, "redis is unreachable"))
        };
        var context = CreateContext(permissions: [], requireSudo: true, session: LoginSession());

        var run = await RunAsync(context, invoker);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, run.Status);
        Assert.False(run.NextCalled);
        Assert.Equal("AUTH_SUDO_UNAVAILABLE", run.Body!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MissingSession_FailsClosedWithoutCallingTheAuthService()
    {
        var invoker = new FakeCallInvoker { ValidateSudo = _ => new DyValidateSudoResponse { Valid = true } };
        var context = CreateContext(permissions: [], requireSudo: true, session: null);

        var run = await RunAsync(context, invoker);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, run.Status);
        Assert.False(run.NextCalled);
        Assert.Equal(0, invoker.ValidateSudoCalls);
    }

    [Fact]
    public async Task UnauthenticatedRequest_KeepsTheExistingUnauthorizedBody()
    {
        var invoker = new FakeCallInvoker { ValidateSudo = _ => new DyValidateSudoResponse { Valid = true } };
        var context = CreateContext(permissions: [], requireSudo: true, session: LoginSession(), authenticated: false);

        var run = await RunAsync(context, invoker);

        Assert.Equal(StatusCodes.Status401Unauthorized, run.Status);
        Assert.False(run.NextCalled);
        Assert.Equal(0, invoker.ValidateSudoCalls);
        Assert.Equal("UNAUTHORIZED", run.Body!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PermissionDenial_WinsBeforeTheSudoCheckAndKeepsItsBody()
    {
        var invoker = new FakeCallInvoker
        {
            ValidateSudo = _ => new DyValidateSudoResponse { Valid = true },
            HasPermission = _ => new DyHasPermissionResponse { HasPermission = false }
        };
        var context = CreateContext(
            permissions: [new AskPermissionAttribute("custom.apps.secrets.manage")],
            requireSudo: true,
            session: LoginSession()
        );

        var run = await RunAsync(context, invoker);

        Assert.Equal(StatusCodes.Status403Forbidden, run.Status);
        Assert.False(run.NextCalled);
        Assert.Equal(0, invoker.ValidateSudoCalls);
        Assert.Equal(1, invoker.HasPermissionCalls);
        Assert.Equal("FORBIDDEN", run.Body!.Value.GetProperty("code").GetString());
        Assert.Equal(
            "Permission custom.apps.secrets.manage was required.",
            run.Body.Value.GetProperty("message").GetString()
        );
    }

    [Fact]
    public async Task GrantedPermission_StillRequiresElevation()
    {
        var invoker = new FakeCallInvoker
        {
            ValidateSudo = _ => new DyValidateSudoResponse { Valid = false },
            HasPermission = _ => new DyHasPermissionResponse { HasPermission = true }
        };
        var context = CreateContext(
            permissions: [new AskPermissionAttribute("custom.apps.secrets.manage")],
            requireSudo: true,
            session: LoginSession()
        );

        var run = await RunAsync(context, invoker);

        Assert.Equal(StatusCodes.Status403Forbidden, run.Status);
        Assert.False(run.NextCalled);
        Assert.Equal(1, invoker.ValidateSudoCalls);
        Assert.Equal("AUTH_SUDO_REQUIRED", run.Body!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UndecoratedEndpoint_SkipsTheGate()
    {
        var invoker = new FakeCallInvoker { ValidateSudo = _ => new DyValidateSudoResponse { Valid = false } };
        var context = CreateContext(permissions: [], requireSudo: false, session: LoginSession());

        var run = await RunAsync(context, invoker);

        Assert.True(run.NextCalled);
        Assert.Equal(0, invoker.ValidateSudoCalls);
        Assert.Equal(0, invoker.HasPermissionCalls);
    }

    private static DyAuthSession LoginSession() => new()
    {
        Id = SessionId,
        AccountId = AccountId,
        Type = DySessionType.DyLogin
    };

    private static HttpContext CreateContext(
        IReadOnlyList<AskPermissionAttribute> permissions,
        bool requireSudo,
        DyAuthSession? session,
        bool authenticated = true,
        bool superuser = false
    )
    {
        var metadata = new List<object>(permissions);
        if (requireSudo) metadata.Add(new RequireSudoAttribute());

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "test-endpoint"
        ));

        if (authenticated) context.Items["CurrentUser"] = new DyAccount { Id = AccountId, IsSuperuser = superuser };
        if (session is not null) context.Items["CurrentSession"] = session;

        return context;
    }

    private static async Task<SudoRun> RunAsync(HttpContext context, FakeCallInvoker invoker)
    {
        var nextCalled = false;
        var middleware = new RemotePermissionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            new DyPermissionService.DyPermissionServiceClient(invoker),
            new DyAuthService.DyAuthServiceClient(invoker),
            NullLogger<RemotePermissionMiddleware>.Instance
        );

        context.Response.Body.Position = 0;
        var payload = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var body = string.IsNullOrEmpty(payload)
            ? (JsonElement?)null
            : JsonDocument.Parse(payload).RootElement.Clone();

        return new SudoRun(
            context.Response.StatusCode,
            nextCalled,
            invoker.ValidateSudoCalls,
            invoker.HasPermissionCalls,
            body
        );
    }

    private sealed record SudoRun(
        int Status,
        bool NextCalled,
        int ValidateSudoCalls,
        int HasPermissionCalls,
        JsonElement? Body
    );

    private sealed class FakeCallInvoker : CallInvoker
    {
        public Func<DyValidateSudoRequest, DyValidateSudoResponse>? ValidateSudo { get; init; }
        public Exception? ValidateSudoError { get; init; }
        public Func<DyHasPermissionRequest, DyHasPermissionResponse>? HasPermission { get; init; }

        public int ValidateSudoCalls { get; private set; }
        public int HasPermissionCalls { get; private set; }
        public DyValidateSudoRequest? LastValidateSudoRequest { get; private set; }
        public CallOptions? LastValidateSudoOptions { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            if (request is DyValidateSudoRequest sudoRequest)
            {
                ValidateSudoCalls++;
                LastValidateSudoRequest = sudoRequest;
                LastValidateSudoOptions = options;

                var failure = ValidateSudoError;
                if (failure is not null)
                    return Unary<TResponse>(Task.FromException<TResponse>(failure));

                var response = ValidateSudo?.Invoke(sudoRequest) ?? new DyValidateSudoResponse();
                return Unary<TResponse>(Task.FromResult((TResponse)(object)response));
            }

            if (request is DyHasPermissionRequest permissionRequest)
            {
                HasPermissionCalls++;
                var response = HasPermission?.Invoke(permissionRequest) ?? new DyHasPermissionResponse();
                return Unary<TResponse>(Task.FromResult((TResponse)(object)response));
            }

            throw new NotSupportedException($"Unexpected unary call to {method.FullName}.");
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request) => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request) => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options) => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options) => throw new NotSupportedException();

        private static AsyncUnaryCall<T> Unary<T>(Task<T> response) => new(
            response,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { }
        );
    }
}
