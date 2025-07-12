using dotnet_etcd;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Components;

namespace DysonNetwork.Pass.Email;

public class EmailService
{
    private readonly PusherService.PusherServiceClient _client;
    private readonly RazorViewRenderer _viewRenderer;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        EtcdClient etcd,
        RazorViewRenderer viewRenderer,
        IConfiguration configuration,
        ILogger<EmailService> logger,
        PusherService.PusherServiceClient client
    )
    {
        _client = GrpcClientHelper.CreatePusherServiceClient(
            etcd,
            configuration["Service:CertPath"]!,
            configuration["Service:KeyPath"]!
        ).GetAwaiter().GetResult();
        _viewRenderer = viewRenderer;
        _logger = logger;
        _client = client;
    }
    
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
            var htmlBody = await _viewRenderer.RenderComponentToStringAsync<TComponent, TModel>(model);
            await SendEmailAsync(recipientName, recipientEmail, subject, htmlBody);
        }
        catch (Exception err)
        {
            _logger.LogError(err, "Failed to render email template...");
            throw;
        }
    }
}