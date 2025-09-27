using System.Globalization;
using System.Text;

namespace DysonNetwork.Shared.Data;

public abstract partial class TextSanitizer
{
    public static string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // List of control characters to preserve
        var preserveControlChars = new[] { '\n', '\r', '\t', ' ' };

        var filtered = new StringBuilder();
        foreach (var ch in from ch in text
                 let category = CharUnicodeInfo.GetUnicodeCategory(ch)
                 where category is not UnicodeCategory.Control || preserveControlChars.Contains(ch)
                 where category is not (UnicodeCategory.Format or UnicodeCategory.NonSpacingMark)
                 select ch)
        {
            filtered.Append(ch);
        }

        return filtered.ToString();
    }
}