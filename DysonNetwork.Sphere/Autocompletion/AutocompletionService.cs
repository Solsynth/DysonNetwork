using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Autocompletion;

public class AutocompletionService(AppDatabase db)
{
    public async Task<List<DysonNetwork.Shared.Models.Autocompletion>> GetAutocompletion(string content, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        if (content.StartsWith('@'))
        {
            var afterAt = content[1..];
            string type;
            string query;
            if (afterAt.Contains('/'))
            {
                var parts = afterAt.Split('/', 2);
                type = parts[0];
                query = parts.Length > 1 ? parts[1] : "";
            }
            else
            {
                type = "u";
                query = afterAt;
            }
            return await AutocompleteAt(type, query, limit);
        }

        if (!content.StartsWith(':')) return [];
        {
            var query = content[1..];
            return await AutocompleteSticker(query, limit);
        }

    }

    private async Task<List<DysonNetwork.Shared.Models.Autocompletion>> AutocompleteAt(string type, string query, int limit)
    {
        var results = new List<DysonNetwork.Shared.Models.Autocompletion>();

        switch (type)
        {
            case "p":
                var publishers = await db.Publishers
                    .Where(p => EF.Functions.Like(p.Name, $"{query}%") || EF.Functions.Like(p.Nick, $"{query}%"))
                    .Take(limit)
                    .Select(p => new DysonNetwork.Shared.Models.Autocompletion
                    {
                        Type = "publisher",
                        Keyword = p.Name,
                        Data = p
                    })
                    .ToListAsync();
                results.AddRange(publishers);
                break;

            case "r":
                var realms = await db.Realms
                    .Where(r => EF.Functions.Like(r.Slug, $"{query}%") || EF.Functions.Like(r.Name, $"{query}%"))
                    .Take(limit)
                    .Select(r => new DysonNetwork.Shared.Models.Autocompletion
                    {
                        Type = "realm",
                        Keyword = r.Slug,
                        Data = r
                    })
                    .ToListAsync();
                results.AddRange(realms);
                break;

            case "c":
                var chats = await db.ChatRooms
                    .Where(c => c.Name != null && EF.Functions.Like(c.Name, $"{query}%"))
                    .Take(limit)
                    .Select(c => new DysonNetwork.Shared.Models.Autocompletion
                    {
                        Type = "chat",
                        Keyword = c.Name!,
                        Data = c
                    })
                    .ToListAsync();
                results.AddRange(chats);
                break;
        }

        return results;
    }

    private async Task<List<DysonNetwork.Shared.Models.Autocompletion>> AutocompleteSticker(string query, int limit)
    {
        var stickers = await db.Stickers
            .Include(s => s.Pack)
            .Where(s => EF.Functions.Like(s.Slug, $"{query}%"))
            .Take(limit)
            .Select(s => new DysonNetwork.Shared.Models.Autocompletion
            {
                Type = "sticker",
                Keyword = s.Slug,
                Data = s
            })
            .ToListAsync();

        // Also possibly search by pack prefix? But user said slug after :
        // Perhaps combine or search packs
        var packs = await db.StickerPacks
            .Where(p => EF.Functions.Like(p.Prefix, $"{query}%"))
            .Take(limit)
            .Select(p => new DysonNetwork.Shared.Models.Autocompletion
            {
                Type = "sticker_pack",
                Keyword = p.Prefix,
                Data = p
            })
            .ToListAsync();

        var results = stickers.Concat(packs).Take(limit).ToList();
        return results;
    }
}
