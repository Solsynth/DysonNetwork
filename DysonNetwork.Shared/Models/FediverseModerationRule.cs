using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Description { get; set; }

    public FediverseModerationRuleType Type { get; set; }

    public FediverseModerationAction Action { get; set; }

    [MaxLength(2048)]
    public string? Domain { get; set; }

    [MaxLength(4096)]
    public string? KeywordPattern { get; set; }

    public bool IsRegex { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int? ReportThreshold { get; set; }

    public Instant? ExpiresAt { get; set; }

    public int Priority { get; set; } = 0;

    public bool IsSystemRule { get; set; } = false;
}
