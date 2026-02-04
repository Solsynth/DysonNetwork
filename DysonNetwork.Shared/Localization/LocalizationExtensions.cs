using System.Globalization;

namespace DysonNetwork.Shared.Localization;

public static class LocalizationExtensions
{
    public static string Localize(this string key, string? locale = null, object? args = null)
    {
        var service = LocalizationServiceLocator.Service;
        if (service == null)
        {
            return key;
        }
        return service.Get(key, locale, args);
    }

    public static string LocalizeCount(this string key, int count, string? locale = null, object? additionalArgs = null)
    {
        object args;
        if (additionalArgs != null)
        {
            args = new CountWrapper(count, additionalArgs);
        }
        else
        {
            args = new { count };
        }
        
        var service = LocalizationServiceLocator.Service;
        if (service == null)
        {
            return key;
        }
        return service.Get(key, locale, args);
    }

    public class CountWrapper
    {
        public int Count { get; }
        
        private readonly object? _additionalArgs;

        public CountWrapper(int count, object? additionalArgs)
        {
            Count = count;
            _additionalArgs = additionalArgs;
        }
    }
}

public static class LocalizationServiceLocator
{
    public static ILocalizationService? Service { get; set; }
}
