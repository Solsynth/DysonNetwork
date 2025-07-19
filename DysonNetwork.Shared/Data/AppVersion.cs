namespace DysonNetwork.Shared.Data;

public record class AppVersion
{
    public required string Version { get; init; }
    public required string Commit { get; init; }
    public required DateTimeOffset UpdateDate { get; init; }
}
