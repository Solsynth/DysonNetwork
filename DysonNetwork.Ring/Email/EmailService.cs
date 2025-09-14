using MailKit.Net.Smtp;
using MimeKit;

namespace DysonNetwork.Ring.Email;

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
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        var cfg = configuration.GetSection("Email").Get<EmailServiceConfiguration>();
        _configuration = cfg ?? throw new ArgumentException("Email service was not configured.");
        _logger = logger;
    }

    public async Task SendEmailAsync(string? recipientName, string recipientEmail, string subject, string htmlBody)
    {
        subject = $"[{_configuration.SubjectPrefix}] {subject}";
        
        _logger.LogInformation($"Sending email to {recipientEmail} with subject {subject}");

        var emailMessage = new MimeMessage();
        emailMessage.From.Add(new MailboxAddress(_configuration.FromName, _configuration.FromAddress));
        emailMessage.To.Add(new MailboxAddress(recipientName, recipientEmail));
        emailMessage.Subject = subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };

        emailMessage.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_configuration.Server, _configuration.Port, _configuration.UseSsl);
        await client.AuthenticateAsync(_configuration.Username, _configuration.Password);
        await client.SendAsync(emailMessage);
        await client.DisconnectAsync(true);
        
        _logger.LogInformation($"Email {subject} sent for {recipientEmail}");
    }

    private static string _ConvertHtmlToPlainText(string html)
    {
        // Remove style tags and their contents
        html = System.Text.RegularExpressions.Regex.Replace(html, "<style[^>]*>.*?</style>", "", 
            System.Text.RegularExpressions.RegexOptions.Singleline);
    
        // Replace header tags with text + newlines
        html = System.Text.RegularExpressions.Regex.Replace(html, "<h[1-6][^>]*>(.*?)</h[1-6]>", "$1\n\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    
        // Replace line breaks
        html = html.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n");
    
        // Remove all remaining HTML tags
        html = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
    
        // Decode HTML entities
        html = System.Net.WebUtility.HtmlDecode(html);
    
        // Remove excess whitespace
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ").Trim();
    
        return html;
    }
}