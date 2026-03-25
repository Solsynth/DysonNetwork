using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum AutomodRuleType
{
    LinkBlocklist = 1,
    KeywordBlocklist = 2,
}

public enum AutomodRuleAction
{
    Derank = 1,
    Hide = 2,
    Flag = 3,
}

[Index(nameof(Name), IsUnique = true)]
public class SnAutomodRule : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Description { get; set; }

    public AutomodRuleType Type { get; set; }

    public AutomodRuleAction DefaultAction { get; set; }

    [MaxLength(4096)]
    public string Pattern { get; set; } = string.Empty;

    public bool IsRegex { get; set; }

    public int DerankWeight { get; set; } = 10;

    public bool IsEnabled { get; set; } = true;

    public Instant? ExpiresAt { get; set; }

    public int Priority { get; set; } = 0;
}