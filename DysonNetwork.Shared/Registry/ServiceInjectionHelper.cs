using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Registry;

public static class ServiceInjectionHelper
{
    public static IServiceCollection AddRingService(this IServiceCollection services)
    {
        services.AddGrpcClient<RingService.RingServiceClient>(o =>
        {
            o.Address = new Uri("https://ring");
        });
        
        return services;
    }
    
    public static IServiceCollection AddAccountService(this IServiceCollection services)
    {
        services.AddGrpcClient<AccountService.AccountServiceClient>(o =>
        {
            o.Address = new Uri("https://pass");
        });
        services.AddSingleton<AccountClientHelper>();
        
        services.AddGrpcClient<BotAccountReceiverService.BotAccountReceiverServiceClient>(o =>
        {
            o.Address = new Uri("https://pass");
        });
        
        services.AddGrpcClient<ActionLogService.ActionLogServiceClient>(o =>
        {
            o.Address = new Uri("https://pass");
        }); 
        
        services.AddGrpcClient<PaymentService.PaymentServiceClient>(o =>
        {
            o.Address = new Uri("https://pass");
        });
        
        return services;
    }
    
    public static IServiceCollection AddDriveService(this IServiceCollection services)
    {
        services.AddGrpcClient<FileService.FileServiceClient>(o =>
        {
            o.Address = new Uri("https://drive");
        });       
        
        services.AddGrpcClient<FileReferenceService.FileReferenceServiceClient>(o =>
        {
            o.Address = new Uri("https://drive");
        });
        
        return services;
    }
    
    public static IServiceCollection AddPublisherService(this IServiceCollection services)
    {
        services.AddGrpcClient<PublisherService.PublisherServiceClient>(o =>
        {
            o.Address = new Uri("https://sphere");
        });
        
        return services;
    }

    public static IServiceCollection AddDevelopService(this IServiceCollection services)
    {
        services.AddGrpcClient<CustomAppService.CustomAppServiceClient>(o =>
        {
            o.Address = new Uri("https://develop");
        });
        
        return services;
    }
 }