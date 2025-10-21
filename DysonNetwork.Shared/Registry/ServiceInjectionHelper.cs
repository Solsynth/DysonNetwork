using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Registry;

public static class ServiceInjectionHelper
{
    public static IServiceCollection AddRingService(this IServiceCollection services)
    {
        services
            .AddGrpcClient<RingService.RingServiceClient>(o => o.Address = new Uri("https://_grpc.ring"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        return services;
    }

    public static IServiceCollection AddAuthService(this IServiceCollection services)
    {
        services
            .AddGrpcClient<AuthService.AuthServiceClient>(o => o.Address = new Uri("https://_grpc.pass"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services
            .AddGrpcClient<PermissionService.PermissionServiceClient>(o => o.Address = new Uri("https://_grpc.pass"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        return services;
    }

    public static IServiceCollection AddAccountService(this IServiceCollection services)
    {
        services
            .AddGrpcClient<AccountService.AccountServiceClient>(o => o.Address = new Uri("https://_grpc.pass"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );
        services.AddSingleton<RemoteAccountService>();

        services
            .AddGrpcClient<BotAccountReceiverService.BotAccountReceiverServiceClient>(o =>
                o.Address = new Uri("https://_grpc.pass")
            )
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services.AddGrpcClient<ActionLogService.ActionLogServiceClient>(o => o.Address = new Uri("https://_grpc.pass"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services.AddGrpcClient<PaymentService.PaymentServiceClient>(o => o.Address = new Uri("https://_grpc.pass"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );
        
        services
            .AddGrpcClient<RealmService.RealmServiceClient>(o => o.Address = new Uri("https://_grpc.pass"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );
        services.AddSingleton<RemoteRealmService>();

        return services;
    }
    
    public static IServiceCollection AddDriveService(this IServiceCollection services)
    {
        services.AddGrpcClient<FileService.FileServiceClient>(o => o.Address = new Uri("https://_grpc.drive"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services.AddGrpcClient<FileReferenceService.FileReferenceServiceClient>(o =>
                o.Address = new Uri("https://_grpc.drive"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        return services;
    }

    public static IServiceCollection AddPublisherService(this IServiceCollection services)
    {
        services
            .AddGrpcClient<PublisherService.PublisherServiceClient>(o => o.Address = new Uri("https://_grpc.sphere"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        return services;
    }

    public static IServiceCollection AddDevelopService(this IServiceCollection services)
    {
        services.AddGrpcClient<CustomAppService.CustomAppServiceClient>(o =>
                o.Address = new Uri("https://_grpc.develop"))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        return services;
    }
}
