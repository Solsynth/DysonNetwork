using System.ServiceModel.Syndication;
using System.Xml;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Markdig;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Zone.SEO;

[ApiController]
public class RssController(PostService.PostServiceClient postClient) : ControllerBase
{
    [HttpGet("rss")]
    [Produces("application/rss+xml")]
    public async Task<IActionResult> Rss()
    {
        var feed = new SyndicationFeed(
            "Solar Network Posts",
            "Latest posts from Solar Network",
            new Uri($"{Request.Scheme}://{Request.Host}/")
        );

        var items = new List<SyndicationItem>();

        // Fetch posts - similar to SitemapController, but for RSS we usually only want recent ones
        // For simplicity, let's fetch the first page of posts
        var request = new ListPostsRequest
        {
            OrderBy = "date",
            OrderDesc = true,
            PageSize = 20 // Get top 20 recent posts
        };

        var response = await postClient.ListPostsAsync(request);

        if (response?.Posts != null)
        {
            foreach (var protoPost in response.Posts)
            {
                var post = SnPost.FromProtoValue(protoPost);
                var postUrl = post.AsUrl(Request.Host.ToString(), Request.Scheme); // Using the extension method

                var item = new SyndicationItem(
                    post.Title,
                    post.Content is not null ? Markdown.ToHtml(post.Content!) : "No content", // Convert Markdown to HTML
                    new Uri(postUrl),
                    post.Id.ToString(),
                    post.EditedAt?.ToDateTimeOffset() ??
                    post.PublishedAt?.ToDateTimeOffset() ?? post.CreatedAt.ToDateTimeOffset()
                )
                {
                    PublishDate = post.PublishedAt?.ToDateTimeOffset() ??
                                  post.CreatedAt.ToDateTimeOffset() // Use CreatedAt for publish date
                };

                items.Add(item);
            }
        }

        feed.Items = items;

        await using var sw = new StringWriter();
        await using var reader = XmlWriter.Create(sw, new XmlWriterSettings { Indent = true, Async = true });

        var formatter = new Rss20FeedFormatter(feed);
        formatter.WriteTo(reader);
        await reader.FlushAsync();

        return Content(sw.ToString(), "application/rss+xml");
    }
}