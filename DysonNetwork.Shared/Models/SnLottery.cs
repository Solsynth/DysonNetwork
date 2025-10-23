using NodaTime;

namespace DysonNetwork.Shared.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public enum LotteryDrawStatus
{
    Pending = 0,
    Drawn = 1
}

public class SnLotteryRecord : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Instant DrawDate { get; set; } // Date of the draw

    [Column(TypeName = "jsonb")]
    public List<int> WinningRegionOneNumbers { get; set; } = new(); // 5 winning numbers

    [Range(0, 99)]
    public int WinningRegionTwoNumber { get; set; } // 1 winning number

    public int TotalTickets { get; set; } // Total tickets processed for this draw
    public int TotalPrizesAwarded { get; set; } // Total prizes awarded
    public long TotalPrizeAmount { get; set; } // Total ISP prize amount awarded
}

public class SnLottery : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public SnAccount Account { get; set; } = null!;
    public Guid AccountId { get; set; }

    [Column(TypeName = "jsonb")]
    public List<int> RegionOneNumbers { get; set; } = new(); // 5 numbers, 0-99, unique

    [Range(0, 99)]
    public int RegionTwoNumber { get; set; } // 1 number, 0-99, can repeat

    public int Multiplier { get; set; } = 1; // Default 1x

    public LotteryDrawStatus DrawStatus { get; set; } = LotteryDrawStatus.Pending; // Status to track draw processing

    public Instant? DrawDate { get; set; } // Date when this ticket was drawn
}
