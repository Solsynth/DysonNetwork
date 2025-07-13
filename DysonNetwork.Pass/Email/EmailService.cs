using dotnet_etcd;
using dotnet_etcd.interfaces;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Components;

namespace DysonNetwork.Pass.Email;

public class EmailService(
    IEtcdClient etcd,
    RazorViewRenderer viewRenderer,
    IConfiguration configuration,
    ILogger<EmailService> logger
)
{
    private readonly PusherService.PusherServiceClient _client = GrpcClientHelper.CreatePusherServiceClient(
        etcd,
        configuration["Service:CertPath"]!,
        configuration["Service:KeyPath"]!
    ).GetAwaiter().GetResult();

    public async Task SendEmailAsync(
        string? recipientName,
        string recipientEmail,
        string subject,
        string htmlBody
    )
    {
        subject = $"[Solarpass] {subject}";

        await _client.SendEmailAsync(
            new SendEmailRequest()
            {
                Email = new EmailMessage()
                {
                    ToName = recipientName,
                    ToAddress = recipientEmail,
                    Subject = subject,
                    Body = htmlBody
                }
            }
        );
    }

    public async Task SendTemplatedEmailAsync<TComponent, TModel>(string? recipientName, string recipientEmail,
        string subject, TModel model)
        where TComponent : IComponent
    {
        try
        {
            var htmlBody = await viewRenderer.RenderComponentToStringAsync<TComponent, TModel>(model);
            await SendEmailAsync(recipientName, recipientEmail, subject, htmlBody);
        }
        catch (Exception err)
        {
            logger.LogError(err, "Failed to render email template...");
            throw;
        }
    }
}