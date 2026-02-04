namespace DysonNetwork.Shared.Localization;

public interface ILocalizationService
{
    string Get(string key, string? locale = null, object? args = null);
}
