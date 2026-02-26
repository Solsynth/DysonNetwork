using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Registry;

public static class ServiceInjectionHelper
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRingService()
        {
            services.AddGrpcClientWithSharedChannel<DyRingService.DyRingServiceClient>(
                "https://_grpc.ring",
                "DyRingService");
            services.AddSingleton<RemoteRingService>();

            return services;
        }

        public IServiceCollection AddAuthService()
        {
            services.AddGrpcClientWithSharedChannel<DyAuthService.DyAuthServiceClient>(
                "https://_grpc.pass",
                "DyAuthService");
            services.AddGrpcClientWithSharedChannel<DyPermissionService.DyPermissionServiceClient>(
                "https://_grpc.pass",
                "DyPermissionService");

            return services;
        }

        public IServiceCollection AddAccountService()
        {
            services.AddGrpcClientWithSharedChannel<DyAccountService.DyAccountServiceClient>(
                "https://_grpc.pass",
                "DyAccountService");
            services.AddSingleton<RemoteAccountService>();

            services.AddGrpcClientWithSharedChannel<DyBotAccountReceiverService.DyBotAccountReceiverServiceClient>(
                "https://_grpc.pass",
                "DyBotAccountReceiverService");
            services.AddGrpcClientWithSharedChannel<DyActionLogService.DyActionLogServiceClient>(
                "https://_grpc.pass",
                "DyActionLogService");
            services.AddGrpcClientWithSharedChannel<DyPaymentService.DyPaymentServiceClient>(
                "https://_grpc.pass",
                "DyPaymentService");
            services.AddGrpcClientWithSharedChannel<DyWalletService.DyWalletServiceClient>(
                "https://_grpc.pass",
                "DyWalletService");
            services.AddGrpcClientWithSharedChannel<DyRealmService.DyRealmServiceClient>(
                "https://_grpc.pass",
                "DyRealmService");
            services.AddSingleton<RemoteRealmService>();

            services.AddGrpcClientWithSharedChannel<DySocialCreditService.DySocialCreditServiceClient>(
                "https://_grpc.pass",
                "DySocialCreditService");

            services.AddGrpcClientWithSharedChannel<DyExperienceService.DyExperienceServiceClient>(
                "https://_grpc.pass",
                "DyExperienceService");

            return services;
        }

        public IServiceCollection AddWalletService()
        {
            services.AddGrpcClientWithSharedChannel<DyWalletService.DyWalletServiceClient>(
                "https://_grpc.wallet",
                "DyWalletService");
            services.AddSingleton<RemoteWalletService>();

            services.AddGrpcClientWithSharedChannel<DyPaymentService.DyPaymentServiceClient>(
                "https://_grpc.wallet",
                "DyPaymentService");
            services.AddSingleton<RemotePaymentService>();

            services.AddGrpcClientWithSharedChannel<DySubscriptionService.DySubscriptionServiceClient>(
                "https://_grpc.wallet",
                "DySubscriptionService");
            services.AddSingleton<RemoteSubscriptionService>();

            return services;
        }

        public IServiceCollection AddDriveService()
        {
            services.AddGrpcClientWithSharedChannel<DyFileService.DyFileServiceClient>(
                "https://_grpc.drive",
                "DyFileService");

            return services;
        }

        public IServiceCollection AddSphereService()
        {
            services.AddGrpcClientWithSharedChannel<DyPostService.DyPostServiceClient>(
                "https://_grpc.sphere",
                "DyPostService");

            services.AddGrpcClientWithSharedChannel<DyPublisherService.DyPublisherServiceClient>(
                "https://_grpc.sphere",
                "DyPublisherService");

            services.AddGrpcClientWithSharedChannel<DyPollService.DyPollServiceClient>(
                "https://_grpc.sphere",
                "DyPollService");
            services.AddSingleton<RemotePublisherService>();

            return services;
        }

        public IServiceCollection AddDevelopService()
        {
            services.AddGrpcClientWithSharedChannel<DyCustomAppService.DyCustomAppServiceClient>(
                "https://_grpc.develop",
                "DyCustomAppService");

            return services;
        }

        public IServiceCollection AddInsightService()
        {
            services.AddGrpcClientWithSharedChannel<DyWebFeedService.DyWebFeedServiceClient>(
                "https://_grpc.insight",
                "DyWebFeedServiceClient");
            services.AddGrpcClientWithSharedChannel<DyWebArticleService.DyWebArticleServiceClient>(
                "https://_grpc.insight",
                "DyWebArticleService");
            services.AddGrpcClientWithSharedChannel<DyWebReaderService.DyWebReaderServiceClient>(
                "https://_grpc.insight",
                "DyWebReaderServiceClient");
            
            services.AddSingleton<RemoteWebFeedService>();
            services.AddSingleton<RemoteWebReaderService>();
            services.AddSingleton<RemoteWebArticleService>();

            return services;
        }
    }
}
