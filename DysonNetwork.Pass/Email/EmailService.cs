using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Components;

namespace DysonNetwork.Pass.Email;

public class EmailService(
    RingService.RingServiceClient pusher,
    RazorViewRenderer viewRenderer,
    ILogger<EmailService> logger
)
{
    public async Task SendEmailAsync(
        string? recipientName,
        string recipientEmail,
        string subject,
        string htmlBody
    )
    {
        await pusher.SendEmailAsync(
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