using System.Net;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.PageData;
using Microsoft.EntityFrameworkCore;
using OpenGraphNet;

namespace DysonNetwork.Pass.Pages.Data;

public class AccountPageData(AppDatabase db, SubscriptionService subscriptions, IConfiguration configuration)
    : IPageDataProvider
{
    private readonly string _siteUrl = configuration["SiteUrl"]!;

    public bool CanHandlePath(PathString path) =>
        path.StartsWithSegments("/accounts") || path.ToString().StartsWith("/@");

    public async Task<IDictionary<string, object?>> GetAppDataAsync(HttpContext context)
    {
        var path = context.Request.Path.Value!;
        var startIndex = path.StartsWith("/accounts/") ? "/accounts/".Length : "/@".Length;
        var endIndex = path.IndexOf('/', startIndex);
        var username = endIndex == -1 ? path[startIndex..] : path.Substring(startIndex, endIndex - startIndex);
        username = WebUtility.UrlDecode(username);
        if (username.StartsWith("@"))
            username = username[1..];

        var account = await db.Accounts
            .Include(e => e.Badges)
            .Include(e => e.Profile)
            .Where(a => a.Name == username)
            .FirstOrDefaultAsync();
        if (account is null) return new Dictionary<string, object?>();

        var perk = await subscriptions.GetPerkSubscriptionAsync(account.Id);
        account.PerkSubscription = perk?.ToReference();

        var og = OpenGraph.MakeGraph(
            title: account.Nick,
            type: "profile",
            image: $"{_siteUrl}/cgi/drive/files/{account.Profile.Picture?.Id}?original=true",
            url: $"{_siteUrl}/@{username}",
            description: account.Profile.Bio ?? $"@{account.Name} profile on the Solar Network",
            siteName: "Solarpass"
        );

        return new Dictionary<string, object?>()
        {
            ["Account"] = account,
            ["OpenGraph"] = og
        };
    }
}