using System.ComponentModel;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using DysonNetwork.Insight.Agent.Foundation;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class WebSearchPlugin(
    IHttpClientFactory httpClientFactory,
    ILogger<WebSearchPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [AgentTool("fetch_url", Description = "Fetch a URL and extract its readable text content. Use this when you need to get the full content of a specific webpage. Returns the page title and main text content.")]
    public async Task<string> FetchUrl(
        [AgentToolParameter("The absolute URL to fetch")] string url,
        [AgentToolParameter("Maximum length of content to return")] int maxLength = 5000)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return JsonSerializer.Serialize(new { success = false, error = "URL cannot be empty" }, JsonOptions);
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                return JsonSerializer.Serialize(new { success = false, error = "Invalid URL format" }, JsonOptions);
            }

            var httpClient = httpClientFactory.CreateClient("WebReader");
            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return JsonSerializer.Serialize(new { success = false, error = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}" }, JsonOptions);
            }

            var html = await response.Content.ReadAsStringAsync();
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(req => req.Content(html));

            var title = document.Title?.Trim() ?? "Untitled";
            
            var content = ExtractReadableContent(document);

            if (content.Length > maxLength)
            {
                content = content[..maxLength] + "...[truncated]";
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                title,
                url,
                content
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch URL: {Url}", url);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("duckduckgo_instant_search", Description = "Quick search using DuckDuckGo Instant Answer API. Best for getting quick facts, definitions, or summaries. Returns abstract information when available.")]
    public async Task<string> DuckDuckGoInstantSearch(
        [AgentToolParameter("Search query")] string query,
        [AgentToolParameter("Maximum number of related topics to return")] string maxResults = "5")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonSerializer.Serialize(new { success = false, error = "Search query cannot be empty" }, JsonOptions);
            }

            query = query.Trim();
            int.TryParse(maxResults, out var limit);
            if (limit <= 0) limit = 5;
            
            var httpClient = httpClientFactory.CreateClient("DuckDuckGo");
            var searchUrl = $"https://api.duckduckgo.com/api?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";

            logger.LogInformation("DuckDuckGo instant search URL: {Url}", searchUrl);
            
            var response = await httpClient.GetStringAsync(searchUrl);
            var data = JsonSerializer.Deserialize<DuckDuckGoInstantResult>(response, JsonOptions);

            if (data == null)
            {
                return JsonSerializer.Serialize(new { success = false, error = "Failed to parse response" }, JsonOptions);
            }

            logger.LogInformation("DuckDuckGo instant search response: AbstractText={HasAbstract}, Answer={HasAnswer}, Definition={HasDefinition}", 
                !string.IsNullOrWhiteSpace(data.AbstractText),
                !string.IsNullOrWhiteSpace(data.Answer),
                !string.IsNullOrWhiteSpace(data.Definition));

            var results = new List<object>();

            if (!string.IsNullOrWhiteSpace(data.AbstractText))
            {
                results.Add(new
                {
                    type = "abstract",
                    title = data.Heading ?? query,
                    text = data.AbstractText,
                    source = data.AbstractSource,
                    url = data.AbstractURL
                });
            }

            if (!string.IsNullOrWhiteSpace(data.Answer))
            {
                results.Add(new
                {
                    type = "answer",
                    title = data.Heading ?? query,
                    text = data.Answer,
                    source = data.AnswerType
                });
            }

            if (!string.IsNullOrWhiteSpace(data.Definition))
            {
                results.Add(new
                {
                    type = "definition",
                    title = data.Heading ?? query,
                    text = data.Definition,
                    source = data.DefinitionSource,
                    url = data.DefinitionURL
                });
            }

            var relatedTopics = data.RelatedTopics?
                .Take(limit)
                .Select(rt => new
                {
                    type = "related",
                    title = ExtractTextBetweenBrackets(rt.Result ?? ""),
                    text = rt.Text,
                    url = rt.FirstURL
                })
                .Where(r => !string.IsNullOrWhiteSpace(r.text))
                .ToList() ?? new();

            results.AddRange(relatedTopics);

            return JsonSerializer.Serialize(new
            {
                success = true,
                query,
                heading = data.Heading,
                results
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DuckDuckGo instant search failed for query: {Query}", query);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [AgentTool("duckduckgo_search", Description = "Full search using DuckDuckGo HTML results. Best for finding specific URLs and links. Returns full result entries with titles, snippets, and complete URLs.")]
    public async Task<string> DuckDuckGoSearch(
        [AgentToolParameter("Search query")] string query,
        [AgentToolParameter("Maximum number of results to return")] string maxResults = "5")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonSerializer.Serialize(new { success = false, error = "Search query cannot be empty" }, JsonOptions);
            }

            query = query.Trim();
            int.TryParse(maxResults, out var limit);
            if (limit <= 0) limit = 5;
            
            var httpClient = httpClientFactory.CreateClient("DuckDuckGo");
            var searchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";

            logger.LogInformation("DuckDuckGo search URL: {Url}", searchUrl);

            var response = await httpClient.GetStringAsync(searchUrl);

            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(req => req.Content(response));

            logger.LogInformation("DuckDuckGo search parsed HTML, results found: {Count}", document.QuerySelectorAll(".result").Length);

            var results = new List<object>();
            int count = 0;

            foreach (var result in document.QuerySelectorAll(".result"))
            {
                if (count >= limit) break;

                var titleElement = result.QuerySelector(".result__title");
                var snippetElement = result.QuerySelector(".result__snippet");

                var title = titleElement?.TextContent?.Trim() ?? "No title";
                var link = titleElement?.GetAttribute("href") ?? "";
                var snippet = snippetElement?.TextContent?.Trim() ?? "No snippet";

                if (link.StartsWith("//"))
                    link = "https:" + link;

                if (string.IsNullOrWhiteSpace(link) || link.Contains("duckduckgo.com") || !link.StartsWith("http"))
                    continue;

                results.Add(new
                {
                    type = "search",
                    title,
                    url = link,
                    snippet
                });

                count++;
            }

            if (results.Count == 0)
            {
                return JsonSerializer.Serialize(new { success = false, error = "No results found", debug = $"HTML length: {response.Length}" }, JsonOptions);
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                query,
                count = results.Count,
                results
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DuckDuckGo search failed for query: {Query}", query);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    private static string ExtractReadableContent(IDocument document)
    {
        var content = document.QuerySelector("article")?.TextContent?.Trim() ??
                      document.QuerySelector("main")?.TextContent?.Trim() ??
                      document.QuerySelector(".content")?.TextContent?.Trim() ??
                      document.QuerySelector(".post")?.TextContent?.Trim() ??
                      document.Body?.TextContent?.Trim();

        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        content = System.Text.RegularExpressions.Regex.Replace(content, @"\s+", " ");
        content = System.Text.RegularExpressions.Regex.Replace(content, @"[\n\r\t]+", " ");

        return content.Trim();
    }

    private static string ExtractTextBetweenBrackets(string html)
    {
        var start = html.IndexOf('>');
        var end = html.LastIndexOf('<');
        if (start >= 0 && end > start)
            return html[(start + 1)..end].Trim();
        return html;
    }

    private class DuckDuckGoInstantResult
    {
        public string? AbstractText { get; set; }
        public string? AbstractSource { get; set; }
        public string? AbstractURL { get; set; }
        public string? Answer { get; set; }
        public string? AnswerType { get; set; }
        public string? Definition { get; set; }
        public string? DefinitionSource { get; set; }
        public string? DefinitionURL { get; set; }
        public string? Heading { get; set; }
        public List<DuckDuckGoRelatedTopic>? RelatedTopics { get; set; }
    }

    private class DuckDuckGoRelatedTopic
    {
        public string? FirstURL { get; set; }
        public string? Result { get; set; }
        public string? Text { get; set; }
    }
}
