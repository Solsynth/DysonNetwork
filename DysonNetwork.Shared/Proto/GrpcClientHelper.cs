using System.Net;
using Grpc.Net.Client;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;

namespace DysonNetwork.Shared.Proto;

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