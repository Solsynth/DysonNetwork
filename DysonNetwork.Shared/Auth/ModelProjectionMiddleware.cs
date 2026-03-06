using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Auth;

public class DyAuthModelProjectionOptions
{
    public bool OverrideCurrentUser { get; set; } = true;
    public bool OverrideCurrentSession { get; set; } = true;
    public string CurrentUserModelKey { get; set; } = "CurrentUserModel";
    public string CurrentSessionModelKey { get; set; } = "CurrentSessionModel";
}

public sealed class DyAuthModelProjectionMiddleware(
    RequestDelegate next,
    IOptions<DyAuthModelProjectionOptions> options
)
{
    private readonly DyAuthModelProjectionOptions _options = options.Value;

    public async Task Invoke(HttpContext context)
    {
        SnAccount? modelUser = null;

        if (context.Items["CurrentUser"] is DyAccount protoUser)
        {
            modelUser = ToSnAccount(protoUser);
            context.Items[_options.CurrentUserModelKey] = modelUser;
            if (_options.OverrideCurrentUser)
                context.Items["CurrentUser"] = modelUser;
        }

        if (context.Items["CurrentSession"] is DyAuthSession protoSession)
        {
            var modelSession = ToSnAuthSession(protoSession, modelUser);
            context.Items[_options.CurrentSessionModelKey] = modelSession;
            if (_options.OverrideCurrentSession)
                context.Items["CurrentSession"] = modelSession;
        }

        await next(context);
    }

    private static SnAccount ToSnAccount(DyAccount proto)
    {
        try
        {
            return SnAccount.FromProtoValue(proto);
        }
        catch
        {
            Guid.TryParse(proto.Id, out var accountId);
            return new SnAccount
            {
                Id = accountId,
                Name = proto.Name ?? string.Empty,
                Nick = proto.Nick ?? string.Empty,
                Language = proto.Language ?? string.Empty,
                Region = proto.Region ?? string.Empty,
                IsSuperuser = proto.IsSuperuser,
                ActivatedAt = proto.ActivatedAt?.ToInstant(),
                Profile = new SnAccountProfile
                {
                    AccountId = accountId
                }
            };
        }
    }

    private static SnAuthSession ToSnAuthSession(DyAuthSession proto, SnAccount? projectedUser)
    {
        Guid.TryParse(proto.Id, out var sessionId);
        Guid.TryParse(proto.AccountId, out var accountId);
        Guid? clientId = Guid.TryParse(proto.ClientId, out var parsedClientId) ? parsedClientId : null;
        Guid? parentSessionId = Guid.TryParse(proto.ParentSessionId, out var parsedParentId) ? parsedParentId : null;
        Guid? appId = Guid.TryParse(proto.AppId, out var parsedAppId) ? parsedAppId : null;

        var session = new SnAuthSession
        {
            Id = sessionId,
            AccountId = accountId,
            Account = projectedUser ?? (proto.Account != null ? ToSnAccount(proto.Account) : new SnAccount
            {
                Id = accountId,
                Profile = new SnAccountProfile { AccountId = accountId }
            }),
            Type = proto.Type switch
            {
                DySessionType.DyOauth => SessionType.OAuth,
                DySessionType.DyOidc => SessionType.Oidc,
                _ => SessionType.Login
            },
            LastGrantedAt = proto.LastGrantedAt?.ToInstant(),
            ExpiredAt = proto.ExpiredAt?.ToInstant(),
            IpAddress = string.IsNullOrWhiteSpace(proto.IpAddress) ? null : proto.IpAddress,
            UserAgent = string.IsNullOrWhiteSpace(proto.UserAgent) ? null : proto.UserAgent,
            ClientId = clientId,
            ParentSessionId = parentSessionId,
            AppId = appId,
            Audiences = proto.Audiences.ToList(),
            Scopes = proto.Scopes.ToList()
        };

        if (proto.Client is not null)
        {
            Guid.TryParse(proto.Client.Id, out var protoClientId);
            Guid.TryParse(proto.Client.AccountId, out var clientAccountId);
            session.Client = new SnAuthClient
            {
                Id = protoClientId,
                AccountId = clientAccountId == Guid.Empty ? accountId : clientAccountId,
                DeviceId = proto.Client.DeviceId ?? string.Empty,
                DeviceName = proto.Client.DeviceName ?? string.Empty,
                DeviceLabel = proto.Client.DeviceLabel,
                Platform = (ClientPlatform)proto.Client.Platform
            };
        }

        return session;
    }
}

public static class DyAuthModelProjectionStartup
{
    public static IServiceCollection AddDyAuthModelProjection(
        this IServiceCollection services,
        Action<DyAuthModelProjectionOptions>? configure = null
    )
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<DyAuthModelProjectionOptions>(_ => { });
        return services;
    }

    public static IApplicationBuilder UseDyAuthModelProjection(this IApplicationBuilder app)
    {
        return app.UseMiddleware<DyAuthModelProjectionMiddleware>();
    }
}
