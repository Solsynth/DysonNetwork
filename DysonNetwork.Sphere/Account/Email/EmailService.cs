using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Components;
using MimeKit;

namespace DysonNetwork.Sphere.Account.Email;

public class EmailServiceConfiguration
{
    public string Server { get; set; } = null!;
    public int Port { get; set; }
    public bool UseSsl { get; set; }
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string FromAddress { get; set; } = null!;
    public string FromName { get; set; } = null!;
    public string SubjectPrefix { get; set; } = null!;
}

public class EmailService
{
    private readonly EmailServiceConfiguration _configuration;
    private readonly RazorViewRenderer _viewRenderer;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, RazorViewRenderer viewRenderer, ILogger<EmailService> logger)
    {
        var cfg = configuration.GetSection("Email").Get<EmailServiceConfiguration>();
        _configuration = cfg ?? throw new ArgumentException("Email service was not configured.");
        _viewRenderer = viewRenderer;
        _logger = logger;
    }

    public async Task SendEmailAsync(string? recipientName, string recipientEmail, string subject, string textBody)
    {
        await SendEmailAsync(recipientName, recipientEmail, subject, textBody, null);
    }

    public async Task SendEmailAsync(string? recipientName, string recipientEmail, string subject, string textBody,
        string? htmlBody)
    {
        subject = $"[{_configuration.SubjectPrefix}] {subject}";

        var emailMessage = new MimeMessage();
        emailMessage.From.Add(new MailboxAddress(_configuration.FromName, _configuration.FromAddress));
        emailMessage.To.Add(new MailboxAddress(recipientName, recipientEmail));
        emailMessage.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            TextBody = textBody
        };

        if (!string.IsNullOrEmpty(htmlBody))
            bodyBuilder.HtmlBody = htmlBody;

        emailMessage.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_configuration.Server, _configuration.Port, _configuration.UseSsl);
        await client.AuthenticateAsync(_configuration.Username, _configuration.Password);
        await client.SendAsync(emailMessage);
        await client.DisconnectAsync(true);
    }

    public async Task SendTemplatedEmailAsync<TComponent, TModel>(string? recipientName, string recipientEmail,
        string subject, TModel model, string fallbackTextBody)
        where TComponent : IComponent
    {
        try
        {
            var htmlBody = await _viewRenderer.RenderComponentToStringAsync<TComponent, TModel>(model);
            await SendEmailAsync(recipientName, recipientEmail, subject, fallbackTextBody, htmlBody);
        }
        catch (Exception err)
        {
            _logger.LogError(err, "Failed to render email template...");
            await SendEmailAsync(recipientName, recipientEmail, subject, fallbackTextBody);
        }
    }
}