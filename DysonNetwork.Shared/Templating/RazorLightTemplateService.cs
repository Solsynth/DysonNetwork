using System.Globalization;
using System.Reflection;
using RazorLight;

namespace DysonNetwork.Shared.Templating;

public class RazorLightTemplateService : ITemplateService
{
    private readonly IRazorLightEngine _engine;
    private readonly Assembly _assembly;
    private readonly string _resourceNamespace;
    private readonly List<string> _availableLocales = [];

    public RazorLightTemplateService(Assembly? assembly = null, string? resourceNamespace = null)
    {
        _assembly = assembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        _resourceNamespace = resourceNamespace ?? "DysonNetwork.Pass.Resources.Templates";
        
        DiscoverAvailableLocales();
        
        _engine = new RazorLightEngineBuilder()
            .UseEmbeddedResourcesProject(_assembly, _resourceNamespace)
            .UseMemoryCachingProvider()
            .Build();
    }

    private void DiscoverAvailableLocales()
    {
        var resourceNames = _assembly.GetManifestResourceNames();
        var prefix = $"{_resourceNamespace}.";
        var suffix = ".cshtml";
        
        foreach (var resourceName in resourceNames)
        {
            if (resourceName.StartsWith(prefix) && resourceName.EndsWith(suffix))
            {
                var locale = ExtractLocaleFromResourceName(resourceName, prefix, suffix);
                if (!string.IsNullOrEmpty(locale) && !_availableLocales.Contains(locale))
                {
                    _availableLocales.Add(locale);
                }
            }
        }
    }

    private static string? ExtractLocaleFromResourceName(string resourceName, string prefix, string suffix)
    {
        // Resource name format: Namespace.Locale.TemplateName.cshtml
        // We need to extract the locale part
        var contentPart = resourceName.Substring(prefix.Length, resourceName.Length - prefix.Length - suffix.Length);
        
        // Split by dot to get locale (first part before template name)
        var parts = contentPart.Split('.');
        return parts.Length >= 2 ?
            // First part is locale (e.g., "en", "zh-hans")
            parts[0] : null;
    }

    public async Task<string> RenderAsync(string templateName, object model, string? locale = null)
    {
        locale ??= CultureInfo.CurrentUICulture.Name;

        var templateKey = $"{locale}.{templateName}";

        try
        {
            return await _engine.CompileRenderAsync(templateKey, model);
        }
        catch (TemplateNotFoundException)
        {
            // Fallback: try all available locales
            foreach (var availableLocale in _availableLocales)
            {
                if (availableLocale.Equals(locale, StringComparison.OrdinalIgnoreCase))
                    continue;

                var fallbackKey = $"{availableLocale}.{templateName}";
                try
                {
                    return await _engine.CompileRenderAsync(fallbackKey, model);
                }
                catch (TemplateNotFoundException)
                {
                    // Continue to next locale
                }
            }

            return $"[Template not found: {templateName}]";
        }
    }

    public async Task<string> RenderWithLayoutAsync(string templateName, string layoutName, object model, string? locale = null)
    {
        // RazorLight handles layouts via @Layout directive in the template
        // This method is provided for API consistency
        return await RenderAsync(templateName, model, locale);
    }

    public IReadOnlyList<string> GetAvailableLocales()
    {
        return _availableLocales.AsReadOnly();
    }
}
