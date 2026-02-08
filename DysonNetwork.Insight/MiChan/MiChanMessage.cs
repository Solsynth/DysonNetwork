namespace DysonNetwork.Insight.MiChan;

public class MiChanMessage
{
    public string SenderId { get; set; } = null!;
    public string Content { get; set; } = null!;
    public bool IsFromBot { get; set; }
    public DateTime Timestamp { get; set; }
}
