using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DysonNetwork.Shared.Content;

public abstract partial class TextSanitizer
{
    [GeneratedRegex(@"[\u0000-\u001F\u007F\u200B-\u200F\u202A-\u202E\u2060-\u206F\uFFF0-\uFFFF]")]
    private static partial Regex WeirdUnicodeRegex();

    [GeneratedRegex(@"[\r\n]{2,}")]
    private static partial Regex MultiNewlineRegex();

    public static string? Sanitize(string? text)
    {
        if (text is null) return null;

        // Normalize weird Unicode characters
        var cleaned = WeirdUnicodeRegex().Replace(text, "");

        // Normalize bold/italic/fancy unicode letters to ASCII
        cleaned = NormalizeFancyUnicode(cleaned);

        // Replace multiple newlines with a single newline
        cleaned = MultiNewlineRegex().Replace(cleaned, "\n");

        return cleaned;
    }

    private static string NormalizeFancyUnicode(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input.Normalize(NormalizationForm.FormC).Where(c =>
                     char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
            sb.Append(c);

        return sb.ToString();
    }
}