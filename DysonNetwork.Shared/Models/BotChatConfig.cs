using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DysonNetwork.Shared.Models;

/// <summary>
/// Bot chat configuration, linked 1:1 to SnBotAccount via Id.
/// Stores commands manifest, webhook declarations, and behavior flags.
/// </summary>
public class SnBotChatConfig : ModelBase
{
    /// <summary>
    /// Same as BotAccount.Id (1:1 relationship).
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The bot's slash commands (e.g. /help, /status).
    /// </summary>
    [Column(TypeName = "jsonb")]
    public List<SnBotCommand> Commands { get; set; } = [];

    /// <summary>
    /// Webhook endpoints the bot subscribes to.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public List<SnBotWebhook> Webhooks { get; set; } = [];

    /// <summary>
    /// If true, DMs to this bot are automatically approved (no invite needed).
    /// </summary>
    public bool AutoApproveDm { get; set; } = true;

    /// <summary>
    /// If false, the bot cannot participate in chat at all.
    /// </summary>
    public bool SupportChat { get; set; } = true;

    /// <summary>
    /// Which event types the bot subscribes to (e.g. "messages.new", "member.joined").
    /// </summary>
    [Column(TypeName = "jsonb")]
    public List<string> SubscribedEvents { get; set; } = ["messages.new"];
}

/// <summary>
/// A bot slash command definition.
/// </summary>
public class SnBotCommand
{
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Usage { get; set; }

    public List<SnBotCommandParameter>? Parameters { get; set; }
}

/// <summary>
/// A parameter for a bot command.
/// </summary>
public class SnBotCommandParameter
{
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Description { get; set; }

    public bool Required { get; set; }

    [MaxLength(64)]
    public string? Type { get; set; } // "string", "int", "user", etc.
}

/// <summary>
/// A webhook endpoint configuration for a bot.
/// </summary>
public class SnBotWebhook
{
    [MaxLength(2048)]
    public string Url { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Secret { get; set; } // HMAC signing secret

    [Column(TypeName = "jsonb")]
    public List<string> Events { get; set; } = ["messages.new"];

    public bool IsActive { get; set; } = true;
}
