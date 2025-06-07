using System.Globalization;
using System.Text;

namespace DysonNetwork.Sphere.Storage;

public abstract class TextSanitizer
{
    public static string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var filtered = new StringBuilder();
        foreach (var ch in from ch in text
                 let category = CharUnicodeInfo.GetUnicodeCategory(ch)
                 where category is not (UnicodeCategory.Control or UnicodeCategory.Format
                     or UnicodeCategory.NonSpacingMark)
                 select ch)
        {
            filtered.Append(ch);
        }
        
        return filtered.ToString(); 
    }
}