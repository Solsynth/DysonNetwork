using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Registry;

public static class ServiceInjectionHelper
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRingService()
        {
            services.AddGrpcClientWithSharedChannel<RingService.RingServiceClient>(
                "https://_grpc.ring",
                "RingService");
            services.AddSingleton<RemoteRingService>();

            return services;
        }

        public IServiceCollection AddAuthService()
        {
            services.AddGrpcClientWithSharedChannel<AuthService.AuthServiceClient>(
                "https://_grpc.pass",
                "AuthService");
            services.AddGrpcClientWithSharedChannel<PermissionService.PermissionServiceClient>(
                "https://_grpc.pass",
                "PermissionService");

            return services;
        }

        public IServiceCollection AddAccountService()
        {
            services.AddGrpcClientWithSharedChannel<AccountService.AccountServiceClient>(
                "https://_grpc.pass",
                "AccountService");
            services.AddSingleton<RemoteAccountService>();

            services.AddGrpcClientWithSharedChannel<BotAccountReceiverService.BotAccountReceiverServiceClient>(
                "https://_grpc.pass",
                "BotAccountReceiverService");
            services.AddGrpcClientWithSharedChannel<ActionLogService.ActionLogServiceClient>(
                "https://_grpc.pass",
                "ActionLogService");
            services.AddGrpcClientWithSharedChannel<PaymentService.PaymentServiceClient>(
                "https://_grpc.pass",
                "PaymentService");
            services.AddGrpcClientWithSharedChannel<WalletService.WalletServiceClient>(
                "https://_grpc.pass",
                "WalletService");
            services.AddGrpcClientWithSharedChannel<RealmService.RealmServiceClient>(
                "https://_grpc.pass",
                "RealmService");
            services.AddSingleton<RemoteRealmService>();

            services.AddGrpcClientWithSharedChannel<SocialCreditService.SocialCreditServiceClient>(
                "https://_grpc.pass",
                "SocialCreditService");

            services.AddGrpcClientWithSharedChannel<ExperienceService.ExperienceServiceClient>(
                "https://_grpc.pass",
                "ExperienceService");

            return services;
        }

        public IServiceCollection AddDriveService()
        {
            services.AddGrpcClientWithSharedChannel<FileService.FileServiceClient>(
                "https://_grpc.drive",
                "FileService");

            return services;
        }

        public IServiceCollection AddSphereService()
        {
            services.AddGrpcClientWithSharedChannel<PostService.PostServiceClient>(
                "https://_grpc.sphere",
                "PostService");

            services.AddGrpcClientWithSharedChannel<PublisherService.PublisherServiceClient>(
                "https://_grpc.sphere",
                "PublisherService");

            services.AddGrpcClientWithSharedChannel<PollService.PollServiceClient>(
                "https://_grpc.sphere",
                "PollService");
            services.AddSingleton<RemotePublisherService>();

            return services;
        }

        public IServiceCollection AddDevelopService()
        {
            services.AddGrpcClientWithSharedChannel<CustomAppService.CustomAppServiceClient>(
                "https://_grpc.develop",
                "CustomAppService");

            return services;
        }

        public IServiceCollection AddInsightService()
        {
            services.AddGrpcClientWithSharedChannel<WebFeedService.WebFeedServiceClient>(
                "https://_grpc.insight",
                "WebFeedServiceClient");
            services.AddGrpcClientWithSharedChannel<WebArticleService.WebArticleServiceClient>(
                "https://_grpc.insight",
                "WebArticleService");
            services.AddGrpcClientWithSharedChannel<WebReaderService.WebReaderServiceClient>(
                "https://_grpc.insight",
                "WebReaderServiceClient");
            
            services.AddSingleton<RemoteWebFeedService>();
            services.AddSingleton<RemoteWebReaderService>();
            services.AddSingleton<RemoteWebArticleService>();

            return services;
        }
    }
}
