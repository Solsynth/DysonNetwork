using MailKit.Net.Smtp;
using MimeKit;

namespace DysonNetwork.Sphere.Account;

public class EmailServiceConfiguration
{
    public string Server { get; set; }
    public int Port { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string FromAddress { get; set; }
    public string FromName { get; set; }
    public string SubjectPrefix { get; set; }
}

public class EmailService
{
    private readonly EmailServiceConfiguration _configuration;

    public EmailService(IConfiguration configuration)
    {
        var cfg = configuration.GetSection("Email").Get<EmailServiceConfiguration>();
        _configuration = cfg ?? throw new ArgumentException("Email service was not configured.");
    }

    public async Task SendEmailAsync(string? recipientName, string recipientEmail, string subject, string textBody)
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

        emailMessage.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_configuration.Server, _configuration.Port, true);
        await client.AuthenticateAsync(_configuration.Username, _configuration.Password);
        await client.SendAsync(emailMessage);
        await client.DisconnectAsync(true);
    }
}