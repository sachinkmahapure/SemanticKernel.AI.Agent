using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AI.ChatAgent.Configuration;
using AI.ChatAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace AI.ChatAgent.Plugins;

/// <summary>
/// Web search plugin supporting both Serper (Google) and Bing Search APIs.
/// Falls back gracefully when API keys are not configured.
/// </summary>
public sealed class WebSearchPlugin(
    IHttpClientFactory httpClientFactory,
    IOptions<WebSearchOptions> options,
    ILogger<WebSearchPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    /// <summary>Search the web for current information.</summary>
    [KernelFunction(nameof(Search))]
    [Description("Search the web for up-to-date information on any topic. Returns titles, URLs, and snippets.")]
    public async Task<string> Search(
        [Description("Search query")] string query,
        [Description("Number of results (default 5)")] int numResults = 5,
        CancellationToken ct = default)
    {
        logger.LogInformation("WEB:Search query={Query} n={N}", query, numResults);

        var provider = options.Value.Provider.ToLowerInvariant();

        var results = provider switch
        {
            "serper" => await SearchWithSerper(query, numResults, ct),
            "bing"   => await SearchWithBing(query, numResults, ct),
            _        => await SearchWithSerper(query, numResults, ct)
        };

        if (results.Count == 0)
            return "{\"results\": [], \"message\": \"No results found or search API not configured.\"}";

        return JsonSerializer.Serialize(new { query, resultCount = results.Count, results });
    }

    /// <summary>Get recent news on a topic.</summary>
    [KernelFunction(nameof(SearchNews))]
    [Description("Search for recent news articles on a topic.")]
    public async Task<string> SearchNews(
        [Description("News search query")] string query,
        [Description("Number of news items (default 5)")] int numResults = 5,
        CancellationToken ct = default)
    {
        logger.LogInformation("WEB:SearchNews query={Query}", query);

        var newsQuery = $"{query} news latest";
        return await Search(newsQuery, numResults, ct);
    }

    /// <summary>Look up a specific URL and return its content summary.</summary>
    [KernelFunction(nameof(FetchUrl))]
    [Description("Fetch the text content of a public web page URL.")]
    public async Task<string> FetchUrl(
        [Description("Full URL to fetch (https://...)")] string url,
        CancellationToken ct = default)
    {
        logger.LogInformation("WEB:FetchUrl url={Url}", url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return $"{{\"error\": \"Invalid URL: {url}\"}}";
        }

        try
        {
            var client = httpClientFactory.CreateClient("ResilienceClient");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI.ChatAgent/1.0");

            var html = await client.GetStringAsync(url, ct);

            // Strip HTML tags for a readable plaintext version
            var text = StripHtml(html);
            var truncated = text.Length > 3000;
            if (truncated) text = text[..3000];

            return JsonSerializer.Serialize(new { url, contentLength = text.Length, truncated, content = text });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WEB:FetchUrl failed for {Url}", url);
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    // ─── Serper ───────────────────────────────────────────────────────────────

    private async Task<List<SearchResult>> SearchWithSerper(
        string query, int num, CancellationToken ct)
    {
        var opts = options.Value.Serper;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            logger.LogWarning("WEB:Serper API key not configured");
            return [];
        }

        var client = httpClientFactory.CreateClient("ResilienceClient");
        var payload = JsonSerializer.Serialize(new { q = query, num = Math.Clamp(num, 1, 10) });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{opts.BaseUrl}/search")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-API-KEY", opts.ApiKey);

        try
        {
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var results = new List<SearchResult>();
            if (doc.RootElement.TryGetProperty("organic", out var organic))
            {
                foreach (var item in organic.EnumerateArray().Take(num))
                {
                    results.Add(new SearchResult
                    {
                        Title   = item.TryGetProperty("title",   out var t) ? t.GetString() ?? "" : "",
                        Url     = item.TryGetProperty("link",    out var l) ? l.GetString() ?? "" : "",
                        Snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() : null,
                        Source  = "Serper"
                    });
                }
            }

            logger.LogInformation("WEB:Serper returned {Count} results", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WEB:Serper search failed");
            return [];
        }
    }

    // ─── Bing ─────────────────────────────────────────────────────────────────

    private async Task<List<SearchResult>> SearchWithBing(
        string query, int num, CancellationToken ct)
    {
        var opts = options.Value.Bing;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            logger.LogWarning("WEB:Bing API key not configured");
            return [];
        }

        var client = httpClientFactory.CreateClient("ResilienceClient");
        var url = $"{opts.BaseUrl}?q={Uri.EscapeDataString(query)}&count={Math.Clamp(num, 1, 10)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", opts.ApiKey);

        try
        {
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var results = new List<SearchResult>();
            if (doc.RootElement.TryGetProperty("webPages", out var pages) &&
                pages.TryGetProperty("value", out var items))
            {
                foreach (var item in items.EnumerateArray().Take(num))
                {
                    results.Add(new SearchResult
                    {
                        Title   = item.TryGetProperty("name",    out var n) ? n.GetString() ?? "" : "",
                        Url     = item.TryGetProperty("url",     out var u) ? u.GetString() ?? "" : "",
                        Snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() : null,
                        Source  = "Bing"
                    });
                }
            }

            logger.LogInformation("WEB:Bing returned {Count} results", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WEB:Bing search failed");
            return [];
        }
    }

    // ─── HTML Stripping ───────────────────────────────────────────────────────

    private static string StripHtml(string html)
    {
        // Remove script/style blocks
        var result = System.Text.RegularExpressions.Regex.Replace(
            html, @"<(script|style)[^>]*>[\s\S]*?</(script|style)>", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove all other tags
        result = System.Text.RegularExpressions.Regex.Replace(result, @"<[^>]+>", " ");

        // Collapse whitespace
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s{2,}", " ");

        return result.Trim();
    }
}
