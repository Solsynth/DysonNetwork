using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;

namespace DysonNetwork.Shared.Models.Embed;

/// <summary>
/// The link embed is a part of the embeddable implementations
/// It can be used in the post or messages' meta's embeds fields
/// </summary>
public class LinkEmbed : EmbeddableBase
{
    public override string Type => "link";

    /// <summary>
    /// The original URL that was processed
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Title of the linked content (from OpenGraph og:title, meta title, or page title)
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Description of the linked content (from OpenGraph og:description or meta description)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// URL to the thumbnail image (from OpenGraph og:image or other meta tags)
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// The favicon URL of the site
    /// </summary>
    public string? FaviconUrl { get; set; }

    /// <summary>
    /// The site name (from OpenGraph og:site_name)
    /// </summary>
    public string? SiteName { get; set; }

    /// <summary>
    /// Type of the content (from OpenGraph og:type)
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Author of the content if available
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Published date of the content if available
    /// </summary>
    public DateTime? PublishedDate { get; set; }

    public Proto.LinkEmbed ToProtoValue()
    {
        var proto = new Proto.LinkEmbed
        {
            Url = Url
        };

        if (!string.IsNullOrEmpty(Title))
            proto.Title = Title;

        if (!string.IsNullOrEmpty(Description))
            proto.Description = Description;

        if (!string.IsNullOrEmpty(ImageUrl))
            proto.ImageUrl = ImageUrl;

        if (!string.IsNullOrEmpty(FaviconUrl))
            proto.FaviconUrl = FaviconUrl;

        if (!string.IsNullOrEmpty(SiteName))
            proto.SiteName = SiteName;

        if (!string.IsNullOrEmpty(ContentType))
            proto.ContentType = ContentType;

        if (!string.IsNullOrEmpty(Author))
            proto.Author = Author;

        if (PublishedDate.HasValue)
            proto.PublishedDate = Timestamp.FromDateTime(PublishedDate.Value.ToUniversalTime());

        return proto;
    }

    public static LinkEmbed FromProtoValue(Proto.LinkEmbed proto)
    {
        return new LinkEmbed
        {
            Url = proto.Url,
            Title = proto.Title == "" ? null : proto.Title,
            Description = proto.Description == "" ? null : proto.Description,
            ImageUrl = proto.ImageUrl == "" ? null : proto.ImageUrl,
            FaviconUrl = proto.FaviconUrl == "" ? null : proto.FaviconUrl,
            SiteName = proto.SiteName == "" ? null : proto.SiteName,
            ContentType = proto.ContentType == "" ? null : proto.ContentType,
            Author = proto.Author == "" ? null : proto.Author,
            PublishedDate = proto.PublishedDate != null ? proto.PublishedDate.ToDateTime() : null
        };
    }
}