using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

[Route(".well-known")]
public class NodeInfoController(
    AppDatabase db,
    IConfiguration configuration
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    [HttpGet("nodeinfo")]
    public IActionResult Discovery()
    {
        return Ok(new
        {
            links = new[]
            {
                new
                {
                    rel = "http://nodeinfo.diaspora.software/ns/schema/2.1",
                    href = $"https://{Domain}/nodeinfo/2.1"
                },
                new
                {
                    rel = "http://nodeinfo.diaspora.software/ns/schema/2.0",
                    href = $"https://{Domain}/nodeinfo/2.0"
                }
            }
        });
    }

    [HttpGet("nodeinfo/2.0")]
    public async Task<IActionResult> NodeInfo2_0()
    {
        var stats = await GetStats();
        return Ok(new
        {
            software = new
            {
                name = "dysonnetwork",
                version = "1.0.0"
            },
            protocols = new[] { "activitypub" },
            services = new
            {
                inbound = Array.Empty<string>(),
                outbound = new[] { "activitypub" }
            },
            usage = new
            {
                users = new
                {
                    total = stats.TotalPublishers,
                    activeMonth = stats.ActivePublishers,
                    activeHalfyear = stats.ActivePublishers
                },
                localPosts = stats.TotalPosts,
                localComments = 0
            },
            openRegistrations = true,
            configuration = new
            {
                urls = new { },
                accounts = new
                {
                    maxFeaturedObjects = 10
                },
                statuses = new
                {
                    maxCharacters = 5000,
                    maxMediaAttachments = 4
                },
                mediaAttachments = new
                {
                    supportedMimeTypes = new[] { "image/*", "video/*", "audio/*" },
                    imageSizeLimit = 10_000_000,
                    imageMatrixLimit = 4_000_000,
                    videoSizeLimit = 40_000_000,
                    videoFrameRateLimit = 30,
                    videoMatrixLimit = 2_000_000
                }
            }
        });
    }

    [HttpGet("nodeinfo/2.1")]
    public async Task<IActionResult> NodeInfo2_1()
    {
        var stats = await GetStats();
        return Ok(new
        {
            version = "2.1",
            software = new
            {
                name = "dysonnetwork",
                version = "1.0.0"
            },
            protocols = new[] { "activitypub" },
            services = new
            {
                inbound = Array.Empty<string>(),
                outbound = new[] { "activitypub" }
            },
            usage = new
            {
                users = new
                {
                    total = stats.TotalPublishers,
                    activeMonth = stats.ActivePublishers,
                    activeHalfyear = stats.ActivePublishers
                },
                localPosts = stats.TotalPosts,
                localComments = 0
            },
            openRegistrations = true,
            configuration = new
            {
                accounts = new
                {
                    maxFeaturedObjects = 10
                },
                statuses = new
                {
                    maxCharacters = 5000,
                    maxMediaAttachments = 4
                },
                mediaAttachments = new
                {
                    supportedMimeTypes = new[] { "image/*", "video/*", "audio/*" },
                    imageSizeLimit = 10_000_000,
                    imageMatrixLimit = 4_000_000,
                    videoSizeLimit = 40_000_000,
                    videoFrameRateLimit = 30,
                    videoMatrixLimit = 2_000_000
                }
            },
            metadata = new { }
        });
    }

    private async Task<NodeInfoStats> GetStats()
    {
        var totalPublishers = await db.Publishers.CountAsync();
        var totalPosts = await db.Posts.CountAsync(p => p.PublisherId != null);
        var totalFediverseActors = await db.FediverseActors.CountAsync(a => a.PublisherId != null);

        return new NodeInfoStats
        {
            TotalPublishers = totalPublishers,
            ActivePublishers = totalFediverseActors,
            TotalPosts = totalPosts
        };
    }

    private class NodeInfoStats
    {
        public int TotalPublishers { get; set; }
        public int ActivePublishers { get; set; }
        public int TotalPosts { get; set; }
    }
}