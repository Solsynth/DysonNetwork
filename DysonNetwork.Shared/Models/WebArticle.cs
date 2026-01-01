using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using NodaTime;
using EmbedLinkEmbed = DysonNetwork.Shared.Models.Embed.LinkEmbed;

namespace DysonNetwork.Shared.Models;

public class SnWebArticle : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(4096)] public string Title { get; set; } = null!;
    [MaxLength(8192)] public string Url { get; set; } = null!;
    [MaxLength(4096)] public string? Author { get; set; }

    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    [Column(TypeName = "jsonb")] public EmbedLinkEmbed? Preview { get; set; }

    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string? Content { get; set; }

    public DateTime? PublishedAt { get; set; }

    public Guid FeedId { get; set; }
    public SnWebFeed Feed { get; set; } = null!;

    public WebArticle ToProtoValue()
    {
        var proto = new WebArticle
        {
            Id = Id.ToString(),
            Title = Title,
            Url = Url,
            FeedId = FeedId.ToString(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };

        if (!string.IsNullOrEmpty(Author))
            proto.Author = Author;

        if (Meta != null)
            proto.Meta = GrpcTypeHelper.ConvertObjectToByteString(Meta);

        if (Preview != null)
            proto.Preview = Preview.ToProtoValue();

        if (!string.IsNullOrEmpty(Content))
            proto.Content = Content;

        if (PublishedAt.HasValue)
            proto.PublishedAt = Timestamp.FromDateTime(PublishedAt.Value.ToUniversalTime());

        if (DeletedAt.HasValue)
            proto.DeletedAt = Timestamp.FromDateTimeOffset(DeletedAt.Value.ToDateTimeOffset());

        return proto;
    }

    public static SnWebArticle FromProtoValue(WebArticle proto)
    {
        return new SnWebArticle
        {
            Id = Guid.Parse(proto.Id),
            Title = proto.Title,
            Url = proto.Url,
            FeedId = Guid.Parse(proto.FeedId),
            Author = proto.Author == "" ? null : proto.Author,
            Meta = proto.Meta != null ? GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object>>(proto.Meta) : null,
            Preview = proto.Preview != null ? EmbedLinkEmbed.FromProtoValue(proto.Preview) : null,
            Content = proto.Content == "" ? null : proto.Content,
            PublishedAt = proto.PublishedAt != null ? proto.PublishedAt.ToDateTime() : null,
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset()),
            DeletedAt = proto.DeletedAt != null ? Instant.FromDateTimeOffset(proto.DeletedAt.ToDateTimeOffset()) : null
        };
    }
}

public class WebFeedConfig
{
    public bool ScrapPage { get; set; }

    public Proto.WebFeedConfig ToProtoValue()
    {
        return new Proto.WebFeedConfig
        {
            ScrapPage = ScrapPage
        };
    }

    public static WebFeedConfig FromProtoValue(Proto.WebFeedConfig proto)
    {
        return new WebFeedConfig
        {
            ScrapPage = proto.ScrapPage
        };
    }
}

public class SnWebFeed : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(8192)] public string Url { get; set; } = null!;
    [MaxLength(4096)] public string Title { get; set; } = null!;
    [MaxLength(8192)] public string? Description { get; set; }

    public Instant? VerifiedAt { get; set; }
    [JsonIgnore] [MaxLength(8192)] public string? VerificationKey { get; set; }

    [Column(TypeName = "jsonb")] public EmbedLinkEmbed? Preview { get; set; }
    [Column(TypeName = "jsonb")] public WebFeedConfig Config { get; set; } = new();

    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;

    [JsonIgnore] public List<SnWebArticle> Articles { get; set; } = new();

    public WebFeed ToProtoValue()
    {
        var proto = new WebFeed
        {
            Id = Id.ToString(),
            Url = Url,
            Title = Title,
            Config = Config.ToProtoValue(),
            PublisherId = PublisherId.ToString(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };

        if (!string.IsNullOrEmpty(Description))
            proto.Description = Description;

        if (VerifiedAt.HasValue)
            proto.VerifiedAt = Timestamp.FromDateTimeOffset(VerifiedAt.Value.ToDateTimeOffset());

        if (Preview != null)
            proto.Preview = Preview.ToProtoValue();

        if (Publisher != null)
            proto.Publisher = Publisher.ToProtoValue();

        if (DeletedAt.HasValue)
            proto.DeletedAt = Timestamp.FromDateTimeOffset(DeletedAt.Value.ToDateTimeOffset());

        return proto;
    }

    public static SnWebFeed FromProtoValue(WebFeed proto)
    {
        return new SnWebFeed
        {
            Id = Guid.Parse(proto.Id),
            Url = proto.Url,
            Title = proto.Title,
            Description = proto.Description == "" ? null : proto.Description,
            VerifiedAt = proto.VerifiedAt != null ? Instant.FromDateTimeOffset(proto.VerifiedAt.ToDateTimeOffset()) : null,
            Preview = proto.Preview != null ? EmbedLinkEmbed.FromProtoValue(proto.Preview) : null,
            Config = WebFeedConfig.FromProtoValue(proto.Config),
            PublisherId = Guid.Parse(proto.PublisherId),
            Publisher = proto.Publisher != null ? SnPublisher.FromProtoValue(proto.Publisher) : null,
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset()),
            DeletedAt = proto.DeletedAt != null ? Instant.FromDateTimeOffset(proto.DeletedAt.ToDateTimeOffset()) : null
        };
    }
}

public class SnWebFeedSubscription : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FeedId { get; set; }
    public SnWebFeed Feed { get; set; } = null!;
    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;

    public WebFeedSubscription ToProtoValue()
    {
        var proto = new WebFeedSubscription
        {
            Id = Id.ToString(),
            FeedId = FeedId.ToString(),
            AccountId = AccountId.ToString(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };

        if (Feed != null)
            proto.Feed = Feed.ToProtoValue();

        return proto;
    }

    public static SnWebFeedSubscription FromProtoValue(WebFeedSubscription proto)
    {
        return new SnWebFeedSubscription
        {
            Id = Guid.Parse(proto.Id),
            FeedId = Guid.Parse(proto.FeedId),
            Feed = proto.Feed != null ? SnWebFeed.FromProtoValue(proto.Feed) : null,
            AccountId = Guid.Parse(proto.AccountId),
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset())
        };
    }
}