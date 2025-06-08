using System.Globalization;
using System.Text;

namespace DysonNetwork.Sphere.Storage;

public abstract class TextSanitizer
{
    public static string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // List of control characters to preserve
        var preserveControlChars = new[] { '\n', '\r', '\t', ' ' };
        
        var filtered = new StringBuilder();
        foreach (var ch in text)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            
            // Keep whitespace and other specified control characters
            if (category is not UnicodeCategory.Control || preserveControlChars.Contains(ch))
            {
                // Still filter out Format and NonSpacingMark categories
                if (category is not (UnicodeCategory.Format or UnicodeCategory.NonSpacingMark))
                {
                    filtered.Append(ch);
                }
            }
        }
        
        return filtered.ToString(); 
    }
}