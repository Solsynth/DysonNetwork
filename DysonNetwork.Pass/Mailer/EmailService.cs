using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Templating;
using Microsoft.AspNetCore.Components;

namespace DysonNetwork.Pass.Mailer;

public class EmailService(
    DyRingService.DyRingServiceClient pusher,
    RazorViewRenderer viewRenderer,
    ITemplateService templateService,
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
            new DySendEmailRequest
            {
                Email = new DyEmailMessage
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

    /// <summary>
    /// Sends an email using a template with locale support.
    /// </summary>
    /// <param name="recipientName">The recipient's display name.</param>
    /// <param name="recipientEmail">The recipient's email address.</param>
    /// <param name="subject">The email subject.</param>
    /// <param name="templateName">The template name (e.g., "welcome", "factor-code").</param>
    /// <param name="model">The model data for the template (anonymous object or any object with public properties).</param>
    /// <param name="locale">Optional locale override (defaults to CurrentUICulture).</param>
    public async Task SendTemplatedEmailAsync(string? recipientName, string recipientEmail,
        string subject, string templateName, object model, string? locale = null)
    {
        try
        {
            var htmlBody = await templateService.RenderAsync(templateName, model, locale);
            await SendEmailAsync(recipientName, recipientEmail, subject, htmlBody);
        }
        catch (Exception err)
        {
            logger.LogError(err, "Failed to render email template {TemplateName} for locale {Locale}",
                templateName, locale ?? "default");
            throw;
        }
    }
}