using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Data;
using NodaTime;

namespace DysonNetwork.Pass.Account;

public enum StatusAttitude
{
    Positive,
    Negative,
    Neutral
}

public class Status : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public StatusAttitude Attitude { get; set; }
    [NotMapped] public bool IsOnline { get; set; }
    [NotMapped] public bool IsCustomized { get; set; } = true;
    public bool IsInvisible { get; set; }
    public bool IsNotDisturb { get; set; }
    [MaxLength(1024)] public string? Label { get; set; }
    public Instant? ClearedAt { get; set; }
    
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
}

public enum CheckInResultLevel
{
    Worst,
    Worse,
    Normal,
    Better,
    Best
}

public class CheckInResult : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CheckInResultLevel Level { get; set; }
    public decimal? RewardPoints { get; set; }
    public int? RewardExperience { get; set; }
    [Column(TypeName = "jsonb")] public ICollection<FortuneTip> Tips { get; set; } = new List<FortuneTip>();
    
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    
    public Instant? BackdatedFrom { get; set; }
}

public class FortuneTip
{
    public bool IsPositive { get; set; }
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
}

/// <summary>
/// This method should not be mapped. Used to generate the daily event calendar.
/// </summary>
public class DailyEventResponse
{
    public Instant Date { get; set; }
    public CheckInResult? CheckInResult { get; set; }
    public ICollection<Status> Statuses { get; set; } = new List<Status>();
}