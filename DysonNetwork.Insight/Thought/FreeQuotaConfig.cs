namespace DysonNetwork.Insight.Thought;

public class FreeQuotaConfig
{
    public bool Enabled { get; set; } = true;
    public int TokensPerDay { get; set; } = 10000;
    public int ResetPeriodHours { get; set; } = 24;
}