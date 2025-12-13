using DysonNetwork.Shared.Proto;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Registry;

public static class ServiceInjectionHelper
{
    public static IServiceCollection AddRingService(this IServiceCollection services, ServiceRegistrar registrar)
    {
        var instance = registrar.GetServiceInstanceAsync("ring", "grpc").GetAwaiter().GetResult();
        services
            .AddGrpcClient<RingService.RingServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        return services;
    }

    public static IServiceCollection AddAuthService(this IServiceCollection services, ServiceRegistrar registrar)
    {
        var instance = registrar.GetServiceInstanceAsync("pass", "grpc").GetAwaiter().GetResult();
        services
            .AddGrpcClient<AuthService.AuthServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services
            .AddGrpcClient<PermissionService.PermissionServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        return services;
    }

    public static IServiceCollection AddAccountService(this IServiceCollection services, ServiceRegistrar registrar)
    {
        var instance = registrar.GetServiceInstanceAsync("pass", "grpc").GetAwaiter().GetResult();
        services
            .AddGrpcClient<AccountService.AccountServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );
        services.AddSingleton<RemoteAccountService>();

        services
            .AddGrpcClient<BotAccountReceiverService.BotAccountReceiverServiceClient>(o =>
                o.Address = new Uri(instance)
            )
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services.AddGrpcClient<ActionLogService.ActionLogServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services.AddGrpcClient<PaymentService.PaymentServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services.AddGrpcClient<WalletService.WalletServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services
            .AddGrpcClient<RealmService.RealmServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );
        services.AddSingleton<RemoteRealmService>();

        services
            .AddGrpcClient<SocialCreditService.SocialCreditServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services
            .AddGrpcClient<ExperienceService.ExperienceServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        return services;
    }

    public static IServiceCollection AddDriveService(this IServiceCollection services, ServiceRegistrar registrar)
    {
        var instance = registrar.GetServiceInstanceAsync("drive", "grpc").GetAwaiter().GetResult();
        services.AddGrpcClient<FileService.FileServiceClient>(o => o.Address = new Uri(instance))
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

    public static IServiceCollection AddSphereService(this IServiceCollection services, ServiceRegistrar registrar)
    {
        var instance = registrar.GetServiceInstanceAsync("drive", "grpc").GetAwaiter().GetResult();
        services
            .AddGrpcClient<PostService.PostServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        services
            .AddGrpcClient<PublisherService.PublisherServiceClient>(o => o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );
        services.AddSingleton<RemotePublisherService>();
        return services;
    }

    public static IServiceCollection AddDevelopService(this IServiceCollection services, ServiceRegistrar registrar)
    {
        var instance = registrar.GetServiceInstanceAsync("develop", "grpc").GetAwaiter().GetResult();
        services.AddGrpcClient<CustomAppService.CustomAppServiceClient>(o =>
                o.Address = new Uri(instance))
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            );

        return services;
    }
}