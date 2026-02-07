using Grpc.Core;
using Grpc.Net.Client;

namespace DysonNetwork.Shared.Registry;

public static class GrpcClientHelper
{
    public static CallInvoker CreateCallInvoker(string url)
    {
        return GrpcChannel.ForAddress(url, new GrpcChannelOptions
        {
            HttpHandler = new HttpClientHandler()
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true },
        }).CreateCallInvoker();
    }
}