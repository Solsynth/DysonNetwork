using NodaTime;

namespace DysonNetwork.Shared.Models;

public class ActivityHeatmap
{
    public string Unit { get; set; } = "posts";

    public Instant PeriodStart { get; set; }
    public Instant PeriodEnd { get; set; }

    public List<ActivityHeatmapItem> Items { get; set; } = new();
}

public class ActivityHeatmapItem
{
    public Instant Date { get; set; }
    public int Count { get; set; }
}