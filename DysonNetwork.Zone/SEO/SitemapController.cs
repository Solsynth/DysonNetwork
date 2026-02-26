using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SimpleMvcSitemap;

namespace DysonNetwork.Zone.SEO;

[ApiController]
public class SitemapController(AppDatabase db, DyPostService.DyPostServiceClient postClient)
    : ControllerBase
{
    [HttpGet("sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {
        var nodes = new List<SitemapNode>
        {
            // Add static pages
            new("/") { ChangeFrequency = ChangeFrequency.Weekly, Priority = 1.0m },
            new("/about") { ChangeFrequency = ChangeFrequency.Monthly, Priority = 0.8m },
            new("/posts") { ChangeFrequency = ChangeFrequency.Daily, Priority = 0.9m },
        };

        // Add dynamic posts
        var allPosts = await GetAllPosts();
        nodes.AddRange(
            allPosts.Select(post =>
            {
                var uri = post.AsUrl(Request.Host.ToString(), Request.Scheme);
                return new SitemapNode(uri)
                {
                    LastModificationDate =
                        post.EditedAt?.ToDateTimeUtc() ?? post.CreatedAt.ToDateTimeUtc(),
                    ChangeFrequency = ChangeFrequency.Monthly,
                    Priority = 0.7m,
                };
            })
        );

        return new SitemapProvider().CreateSitemap(new SitemapModel(nodes));
    }

    private async Task<List<SnPost>> GetAllPosts()
    {
        var allPosts = new List<SnPost>();
        string? pageToken = null;
        const int pageSize = 100; // Fetch in batches

        while (true)
        {
            var request = new DyListPostsRequest
            {
                OrderBy = "date",
                OrderDesc = true,
                PageSize = pageSize,
                PageToken = pageToken ?? string.Empty,
            };

            request.Types_.Add(DyPostType.DyArticle);

            var response = await postClient.ListPostsAsync(request);

            if (response?.Posts != null)
                allPosts.AddRange(response.Posts.Select(SnPost.FromProtoValue));

            if (string.IsNullOrEmpty(response?.NextPageToken))
                break;

            pageToken = response.NextPageToken;
        }

        return allPosts;
    }
}

