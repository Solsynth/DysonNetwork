namespace DysonNetwork.Shared.Localization;

public class LocalizationEntry
{
    public string Value { get; set; } = string.Empty;
    
    public string? One { get; set; }
    
    public string? Other { get; set; }

    public bool IsPlural => One != null || Other != null;
}
