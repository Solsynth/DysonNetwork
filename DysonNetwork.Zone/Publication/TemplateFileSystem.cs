using DotLiquid.FileSystems;

namespace DysonNetwork.Zone.Publication;

public class TemplateFileSystem(string siteDirectory) : IFileSystem
{
    public string ReadTemplateFile(DotLiquid.Context context, string templateName)
    {
        var normalizedName = templateName.Trim().Trim('"', '\'', ' ');

        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new InvalidOperationException("Template name is empty.");

        if (normalizedName.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Path traversal is not allowed.");

        foreach (var relativePath in BuildCandidatePaths(normalizedName))
        {
            var fullPath = BuildValidatedPath(relativePath);
            if (!File.Exists(fullPath))
                continue;

            return File.ReadAllText(fullPath);
        }

        throw new InvalidOperationException($"Template '{templateName}' not found.");
    }

    private IEnumerable<string> BuildCandidatePaths(string templateName)
    {
        var sanitized = templateName.Replace('\\', '/').TrimStart('/');
        var hasLiquidExt = sanitized.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase);

        if (hasLiquidExt)
        {
            yield return sanitized;
            yield return $"templates/{sanitized}";
            yield break;
        }

        yield return $"templates/{sanitized}.html.liquid";
        yield return $"templates/{sanitized}.liquid";
        yield return $"{sanitized}.html.liquid";
        yield return $"{sanitized}.liquid";
    }

    private string BuildValidatedPath(string relativePath)
    {
        var combined = Path.Combine(siteDirectory, relativePath);
        var fullPath = Path.GetFullPath(combined);
        var rootPath = Path.GetFullPath(siteDirectory);

        if (!fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !string.Equals(fullPath, rootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Template path escapes site directory.");
        }

        return fullPath;
    }
}
