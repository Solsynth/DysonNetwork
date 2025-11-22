using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Zone.SEO;

public static class SnPostExtensions
{
    public static string AsUrl(this SnPost post, string host, string scheme)
    {
        return $"{scheme}://{host}/p/{post.Slug ?? post.Id.ToString()}";
    }
}
