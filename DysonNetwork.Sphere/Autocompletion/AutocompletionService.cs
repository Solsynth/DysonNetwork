using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Autocompletion;

public class AutocompletionService(AppDatabase db, RemoteAccountService remoteAccountsHelper)
{
    public async Task<List<DysonNetwork.Shared.Models.Autocompletion>> GetAutocompletion(string content, Guid? chatId = null, Guid? realmId = null, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        if (content.StartsWith('@'))
        {
            var afterAt = content[1..];
            string type;
            string query;
            var hadSlash = afterAt.Contains('/');
            if (hadSlash)
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

            return await AutocompleteAt(type, query, chatId, realmId, hadSlash, limit);
        }

        if (!content.StartsWith(':')) return [];
        {
            var query = content[1..];
            return await AutocompleteSticker(query, limit);
        }
    }

    private async Task<List<DysonNetwork.Shared.Models.Autocompletion>> AutocompleteAt(string type, string query, Guid? chatId, Guid? realmId, bool hadSlash,
        int limit)
    {
        var results = new List<DysonNetwork.Shared.Models.Autocompletion>();

        switch (type)
        {
            case "u":
                var allAccounts = await remoteAccountsHelper.SearchAccounts(query);
                var filteredAccounts = allAccounts;

                if (chatId.HasValue)
                {
                    var chatMemberIds = await db.ChatMembers
                        .Where(m => m.ChatRoomId == chatId.Value && m.JoinedAt != null && m.LeaveAt == null)
                        .Select(m => m.AccountId)
                        .ToListAsync();
                    var chatMemberIdStrings = chatMemberIds.Select(id => id.ToString()).ToHashSet();
                    filteredAccounts = allAccounts.Where(a => chatMemberIdStrings.Contains(a.Id)).ToList();
                }
                else if (realmId.HasValue)
                {
                    var realmMemberIds = await db.RealmMembers
                        .Where(m => m.RealmId == realmId.Value && m.LeaveAt == null)
                        .Select(m => m.AccountId)
                        .ToListAsync();
                    var realmMemberIdStrings = realmMemberIds.Select(id => id.ToString()).ToHashSet();
                    filteredAccounts = allAccounts.Where(a => realmMemberIdStrings.Contains(a.Id)).ToList();
                }

                var users = filteredAccounts
                    .Take(limit)
                    .Select(a => new DysonNetwork.Shared.Models.Autocompletion
                    {
                        Type = "user",
                        Keyword = "@" + (hadSlash ? "u/" : "") + a.Name,
                        Data = SnAccount.FromProtoValue(a)
                    })
                    .ToList();
                results.AddRange(users);
                break;
            case "p":
                var publishers = await db.Publishers
                    .Where(p => EF.Functions.Like(p.Name, $"{query}%") || EF.Functions.Like(p.Nick, $"{query}%"))
                    .Take(limit)
                    .Select(p => new DysonNetwork.Shared.Models.Autocompletion
                    {
                        Type = "publisher",
                        Keyword = "@p/" + p.Name,
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
                        Keyword = "@r/" + r.Slug,
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
                        Keyword = "@c/" + c.Name,
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
            .Where(s => EF.Functions.Like(s.Pack.Prefix + "+" + s.Slug, $"{query}%"))
            .Take(limit)
            .Select(s => new DysonNetwork.Shared.Models.Autocompletion
            {
                Type = "sticker",
                Keyword = $":{s.Pack.Prefix}+{s.Slug}:",
                Data = s
            })
            .ToListAsync();

        var results = stickers.ToList();
        return results;
    }
}
