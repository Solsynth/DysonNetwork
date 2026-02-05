using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace DysonNetwork.Shared.Localization;

public class JsonLocalizationService : ILocalizationService
{
    private readonly Dictionary<string, Dictionary<string, LocalizationEntry>> _localeCache = new();
    private readonly Assembly _assembly;
    private readonly string _resourceNamespace;
    private readonly object _lock = new();
    private readonly List<string> _availableLocales = [];

    public JsonLocalizationService(Assembly? assembly = null, string? resourceNamespace = null)
    {
        _assembly = assembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        _resourceNamespace = resourceNamespace ?? "DysonNetwork.Pass.Resources.Locales";
        DiscoverAvailableLocales();
    }

    private void DiscoverAvailableLocales()
    {
        var resourceNames = _assembly.GetManifestResourceNames();
        var prefix = $"{_resourceNamespace}.";
        var suffix = ".json";

        foreach (var resourceName in resourceNames)
        {
            if (resourceName.StartsWith(prefix) && resourceName.EndsWith(suffix))
            {
                var locale = resourceName.Substring(prefix.Length, resourceName.Length - prefix.Length - suffix.Length);
                _availableLocales.Add(locale);
            }
        }
    }

    public string Get(string key, string? locale = null, object? args = null)
    {
        locale ??= CultureInfo.CurrentUICulture.Name;

        // Try the requested locale first
        var entries = GetLocaleEntries(locale);
        if (entries.TryGetValue(key, out var entry))
        {
            return FormatEntry(entry, args);
        }

        // Fallback: search all available locales
        foreach (var availableLocale in _availableLocales)
        {
            if (availableLocale.Equals(locale, StringComparison.OrdinalIgnoreCase))
                continue;

            var fallbackEntries = GetLocaleEntries(availableLocale);
            if (fallbackEntries.TryGetValue(key, out entry))
            {
                return FormatEntry(entry, args);
            }
        }

        // If no translation found, return key with args joined
        return FormatFallback(key, args);
    }

    private string FormatEntry(LocalizationEntry entry, object? args)
    {
        string template;
        if (args != null && entry.IsPlural)
        {
            template = SelectPluralForm(entry, args);
        }
        else
        {
            template = !string.IsNullOrEmpty(entry.Value) ? entry.Value : (entry.Other ?? entry.One ?? string.Empty);
        }

        return FormatTemplate(template, args);
    }

    private string FormatFallback(string key, object? args)
    {
        if (args == null)
        {
            return key;
        }

        // Extract argument values and join them with the key
        var argValues = new List<string>();
        var type = args.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var value = prop.GetValue(args);
            if (value != null)
            {
                argValues.Add(value.ToString() ?? string.Empty);
            }
        }

        foreach (var field in fields)
        {
            var value = field.GetValue(args);
            if (value != null)
            {
                argValues.Add(value.ToString() ?? string.Empty);
            }
        }

        if (argValues.Count == 0)
        {
            return key;
        }

        return $"{key} {string.Join(" ", argValues)}";
    }

    private Dictionary<string, LocalizationEntry> GetLocaleEntries(string locale)
    {
        lock (_lock)
        {
            if (_localeCache.TryGetValue(locale, out var cached))
            {
                return cached;
            }

            var entries = LoadLocale(locale);
            _localeCache[locale] = entries;
            return entries;
        }
    }

    private Dictionary<string, LocalizationEntry> LoadLocale(string locale)
    {
        var resourceName = $"{_resourceNamespace}.{locale}.json";

        using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return new Dictionary<string, LocalizationEntry>(StringComparer.OrdinalIgnoreCase);
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        if (root == null)
        {
            return new Dictionary<string, LocalizationEntry>(StringComparer.OrdinalIgnoreCase);
        }

        var entries = new Dictionary<string, LocalizationEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in root)
        {
            entries[kvp.Key] = ParseLocalizationEntry(kvp.Value);
        }

        return entries;
    }

    private LocalizationEntry ParseLocalizationEntry(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new LocalizationEntry { Value = element.GetString() ?? string.Empty };
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var entry = new LocalizationEntry();
            if (element.TryGetProperty("value", out var valueProp))
            {
                entry.Value = valueProp.GetString() ?? string.Empty;
            }
            if (element.TryGetProperty("one", out var oneProp))
            {
                entry.One = oneProp.GetString();
            }
            if (element.TryGetProperty("other", out var otherProp))
            {
                entry.Other = otherProp.GetString();
            }
            return entry;
        }

        return new LocalizationEntry { Value = element.ToString() };
    }

    private string SelectPluralForm(LocalizationEntry entry, object args)
    {
        var countValue = GetCountValue(args);
        if (countValue.HasValue)
        {
            var count = countValue.Value;
            if (count == 1 && !string.IsNullOrEmpty(entry.One))
            {
                return entry.One;
            }
        }

        return entry.Other ?? entry.One ?? string.Empty;
    }

    private int? GetCountValue(object args)
    {
        if (args == null) return null;

        var type = args.GetType();
        var countProperty = type.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (countProperty != null)
        {
            var value = countProperty.GetValue(args);
            if (value is int intValue) return intValue;
            if (value is long longValue) return (int)longValue;
            if (value is double doubleValue) return (int)doubleValue;
            if (value is decimal decimalValue) return (int)decimalValue;
        }

        var countField = type.GetField("Count", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (countField != null)
        {
            var value = countField.GetValue(args);
            if (value is int intValue) return intValue;
            if (value is long longValue) return (int)longValue;
            if (value is double doubleValue) return (int)doubleValue;
            if (value is decimal decimalValue) return (int)decimalValue;
        }

        return null;
    }

    private string FormatTemplate(string template, object? args)
    {
        if (args == null || string.IsNullOrEmpty(template))
        {
            return template;
        }

        var result = template;
        var type = args.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var placeholder = $"{{{prop.Name}}}";
            var value = prop.GetValue(args);
            result = result.Replace(placeholder, value?.ToString() ?? string.Empty);
        }

        foreach (var field in fields)
        {
            var placeholder = $"{{{field.Name}}}";
            var value = field.GetValue(args);
            result = result.Replace(placeholder, value?.ToString() ?? string.Empty);
        }

        return result;
    }
}
