namespace DysonNetwork.Drive.Storage;

public class FileReanalysisOptions
{
    public bool Enabled { get; set; } = true;
    public bool ValidateCompression { get; set; } = true;
    public bool ValidateThumbnails { get; set; } = true;
}
