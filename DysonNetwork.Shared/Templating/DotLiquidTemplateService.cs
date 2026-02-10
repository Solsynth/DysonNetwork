using System.Globalization;
using System.Reflection;
using DotLiquid;

namespace DysonNetwork.Shared.Templating;

public class DotLiquidTemplateService : ITemplateService
{
    private readonly Dictionary<string, Template> _templates = new();
    private readonly Assembly _assembly;
    private readonly string _resourceNamespace;
    private readonly List<string> _availableLocales = [];

    public DotLiquidTemplateService(Assembly? assembly = null, string? resourceNamespace = null)
    {
        _assembly = assembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        _resourceNamespace = resourceNamespace ?? "DysonNetwork.Pass.Resources.Templates";

        DiscoverAvailableLocales();
        PreloadTemplates();
    }

    private void DiscoverAvailableLocales()
    {
        var prefix = $"{_resourceNamespace}.";
        foreach (var resourceName in _assembly.GetManifestResourceNames())
        {
            if (resourceName.StartsWith(prefix) && resourceName.EndsWith(".liquid"))
            {
                var locale = ExtractLocaleFromResourceName(resourceName, prefix);
                if (!string.IsNullOrEmpty(locale) && !_availableLocales.Contains(locale))
                    _availableLocales.Add(locale);
            }
        }
    }

    private void PreloadTemplates()
    {
        foreach (var locale in _availableLocales)
        {
            foreach (var templateName in GetTemplateNamesForLocale(locale))
            {
                var key = $"{locale}.{templateName}";
                var resourceName = $"{_resourceNamespace}.{locale}.{templateName}.liquid";
                using var stream = _assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();
                    _templates[key] = Template.Parse(content);
                }
            }
        }
    }

    private IEnumerable<string> GetTemplateNamesForLocale(string locale)
    {
        var prefix = $"{_resourceNamespace}.{locale}.";
        return _assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(prefix) && r.EndsWith(".liquid"))
            .Select(r =>
            {
                var name = r.Substring(prefix.Length, r.Length - prefix.Length - 7);
                return name.Replace("._Layout", "_Layout");
            });
    }

    private static string? ExtractLocaleFromResourceName(string resourceName, string prefix)
    {
        var contentPart = resourceName.Substring(prefix.Length);
        var parts = contentPart.Split('.');
        return parts.Length >= 2 ? parts[0] : null;
    }

    public Task<string> RenderAsync(string templateName, object model, string? locale = null)
    {
        locale ??= CultureInfo.CurrentUICulture.Name;
        var key = $"{locale}.{templateName}";

        if (_templates.TryGetValue(key, out var template))
        {
            var hash = Hash.FromAnonymousObject(model);
            return Task.FromResult(template.Render(hash));
        }

        foreach (var availableLocale in _availableLocales)
        {
            if (availableLocale.Equals(locale, StringComparison.OrdinalIgnoreCase)) continue;
            var fallbackKey = $"{availableLocale}.{templateName}";
            if (_templates.TryGetValue(fallbackKey, out var fallbackTemplate))
            {
                var hash = Hash.FromAnonymousObject(model);
                return Task.FromResult(fallbackTemplate.Render(hash));
            }
        }

        return Task.FromResult($"[Template not found: {templateName}]");
    }

    public Task<string> RenderWithLayoutAsync(string templateName, string layoutName, object model, string? locale = null)
    {
        return RenderAsync(templateName, model, locale);
    }

    public IReadOnlyList<string> GetAvailableLocales() => _availableLocales.AsReadOnly();
}
