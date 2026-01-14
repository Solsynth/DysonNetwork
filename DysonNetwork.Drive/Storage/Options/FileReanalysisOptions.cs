namespace DysonNetwork.Drive.Storage.Options;

public class FileReanalysisOptions
{
    public bool Enabled { get; init; } = true;
    public bool ValidateCompression { get; init; } = true;
    public bool ValidateThumbnails { get; init; } = true;
}
