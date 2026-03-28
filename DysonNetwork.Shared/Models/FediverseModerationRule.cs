using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum FediverseModerationRuleType
{
    DomainBlock = 1,
    DomainAllow = 2,
    KeywordBlock = 3,
    KeywordAllow = 4,
    ReportThreshold = 5,
}

public enum FediverseModerationAction
{
    Block = 1,
    Allow = 2,
    Silence = 3,
    Suspend = 4,
    Flag = 5,
    Derank = 6,
}

[Index(nameof(Domain), IsUnique = false)]
public class SnFediverseModerationRule : ModelBase
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1024)]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("type")]
    public FediverseModerationRuleType Type { get; set; }

    [JsonPropertyName("action")]
    public FediverseModerationAction Action { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [MaxLength(4096)]
    [JsonPropertyName("keyword_pattern")]
    public string? KeywordPattern { get; set; }

    [JsonPropertyName("is_regex")]
    public bool IsRegex { get; set; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("report_threshold")]
    public int? ReportThreshold { get; set; }

    [JsonPropertyName("expires_at")]
    public Instant? ExpiresAt { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("is_system_rule")]
    public bool IsSystemRule { get; set; } = false;
}
