using System.Collections;
using System.Text.Json;
using DotLiquid;

namespace DysonNetwork.Zone.Publication;

public static class LiquidCustomFilters
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static string Json(object? input)
    {
        var normalized = Normalize(input);
        return JsonSerializer.Serialize(normalized, JsonOptions);
    }

    public static string Inspect(object? input)
    {
        return Json(input);
    }

    private static object? Normalize(object? input)
    {
        if (input is null)
            return null;

        if (input is DateTime dt)
            return dt.ToString("O");

        if (input is DateTimeOffset dto)
            return dto.ToString("O");

        if (input is Hash hash)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var keyObj in hash.Keys)
            {
                if (keyObj is not string key)
                    continue;

                dict[key] = Normalize(hash[key]);
            }
            return dict;
        }

        if (input is IDictionary dictionary)
        {
            var dict = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string key)
                    dict[key] = Normalize(entry.Value);
            }
            return dict;
        }

        if (input is IEnumerable enumerable && input is not string)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
                list.Add(Normalize(item));
            return list;
        }

        return input;
    }
}
