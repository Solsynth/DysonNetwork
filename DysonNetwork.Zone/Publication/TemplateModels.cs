using System.Text.Json.Serialization;

namespace DysonNetwork.Zone.Publication;

public enum TemplateResolutionKind
{
    None,
    StaticFile,
    Template,
}

public class TemplateRenderResult
{
    public bool Handled { get; init; }
    public int StatusCode { get; init; } = StatusCodes.Status200OK;
    public string ContentType { get; init; } = "text/html";
    public string? Content { get; init; }
    public string? StaticFilePath { get; init; }
}

public class TemplateRouteResolution
{
    public TemplateResolutionKind Kind { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public int StatusCode { get; init; } = StatusCodes.Status200OK;
    public string PageType { get; init; } = "page";
    public Dictionary<string, string> RouteParams { get; init; } = [];
    public TemplateRouteEntry? RouteEntry { get; init; }
}

public class TemplateRouteManifest
{
    [JsonPropertyName("routes")]
    public List<TemplateRouteEntry> Routes { get; set; } = [];
}

public class TemplateRouteEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "/";

    [JsonPropertyName("template")]
    public string Template { get; set; } = string.Empty;

    [JsonPropertyName("page_type")]
    public string? PageType { get; set; }

    [JsonPropertyName("query_defaults")]
    public Dictionary<string, string>? QueryDefaults { get; set; }

    [JsonPropertyName("data")]
    public TemplateRouteDataOptions? Data { get; set; }
}

public class TemplateRouteDataOptions
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("order_by")]
    public string? OrderBy { get; set; }

    [JsonPropertyName("order_desc")]
    public bool? OrderDesc { get; set; }

    [JsonPropertyName("page_size")]
    public int? PageSize { get; set; }

    [JsonPropertyName("types")]
    public List<string>? Types { get; set; }

    [JsonPropertyName("publisher_ids")]
    public List<string>? PublisherIds { get; set; }

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("query")]
    public string? Query { get; set; }

    [JsonPropertyName("include_replies")]
    public bool? IncludeReplies { get; set; }

    [JsonPropertyName("include_forwards")]
    public bool? IncludeForwards { get; set; }

    [JsonPropertyName("slug_param")]
    public string? SlugParam { get; set; }
}
