using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;
using HtmlAgilityPack;

namespace AI_Interface.Services;

/// <summary>
/// Web search routed to the provider selected in settings (DuckDuckGo / SearXNG / Brave / Tavily /
/// Google), plus page text extraction using HtmlAgilityPack. The injected <see cref="HttpClient"/>
/// is configured with a desktop User-Agent in DI so pages serve their normal markup. The provider
/// and its credentials are read from <see cref="ISettingsService"/> on every call, so changes in the
/// UI take effect without a restart. Missing credentials or transport errors yield an empty result
/// set rather than throwing; API keys are never logged.
/// </summary>
public sealed class WebSearchService : IWebSearchService
{
    private const string DuckDuckGoEndpoint = "https://html.duckduckgo.com/html/";
    private const string BraveEndpoint = "https://api.search.brave.com/res/v1/web/search";
    private const string TavilyEndpoint = "https://api.tavily.com/search";
    private const string GoogleEndpoint = "https://www.googleapis.com/customsearch/v1";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ISettingsService _settings;

    public WebSearchService(HttpClient http, ISettingsService settings)
    {
        _http = http;
        _settings = settings;
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken ct = default)
    {
        var s = _settings.Current;
        return s.SearchProvider switch
        {
            SearchProvider.SearXNG => SearchSearxngAsync(query, maxResults, s.SearxngUrl, ct),
            SearchProvider.Brave => SearchBraveAsync(query, maxResults, s.BraveApiKey, ct),
            SearchProvider.Tavily => SearchTavilyAsync(query, maxResults, s.TavilyApiKey, ct),
            SearchProvider.Google => SearchGoogleAsync(query, maxResults, s.GoogleApiKey, s.GoogleSearchEngineId, ct),
            _ => SearchDuckDuckGoAsync(query, maxResults, ct)
        };
    }

    // --- DuckDuckGo (keyless HTML scraping) ---

    private async Task<IReadOnlyList<SearchResult>> SearchDuckDuckGoAsync(
        string query, int maxResults, CancellationToken ct)
    {
        var url = $"{DuckDuckGoEndpoint}?q={Uri.EscapeDataString(query)}";
        string html;
        try
        {
            html = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        }
        // A timeout surfaces as TaskCanceledException (ct NOT requested); treat it like any transport error.
        // A genuine user cancel (ct requested) is allowed to propagate so the run stops.
        catch (Exception ex) when (ex is HttpRequestException ||
                                   (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return Array.Empty<SearchResult>();
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@class,'result__a')]");
        if (anchors is null)
            return Array.Empty<SearchResult>();

        var snippets = doc.DocumentNode.SelectNodes("//a[contains(@class,'result__snippet')]");

        var results = new List<SearchResult>();
        for (var i = 0; i < anchors.Count && results.Count < maxResults; i++)
        {
            var anchor = anchors[i];
            var href = NormalizeUrl(anchor.GetAttributeValue("href", ""));
            if (string.IsNullOrEmpty(href))
                continue;

            var title = WebUtility.HtmlDecode(anchor.InnerText).Trim();
            var snippet = i < (snippets?.Count ?? 0)
                ? WebUtility.HtmlDecode(snippets![i].InnerText).Trim()
                : "";

            results.Add(new SearchResult { Title = title, Url = href, Snippet = snippet });
        }

        return results;
    }

    // --- SearXNG (GET {instance}/search?q=...&format=json) ---

    private async Task<IReadOnlyList<SearchResult>> SearchSearxngAsync(
        string query, int maxResults, string instanceUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instanceUrl))
            return Array.Empty<SearchResult>();

        var baseUrl = instanceUrl.Trim().TrimEnd('/');
        var url = $"{baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json";

        SearxngResponse? body;
        try
        {
            body = await _http.GetFromJsonAsync<SearxngResponse>(url, JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return Array.Empty<SearchResult>();
        }

        if (body?.Results is null)
            return Array.Empty<SearchResult>();

        var results = new List<SearchResult>();
        foreach (var item in body.Results)
        {
            if (results.Count >= maxResults)
                break;
            if (string.IsNullOrWhiteSpace(item.Url))
                continue;
            results.Add(new SearchResult
            {
                Title = item.Title ?? item.Url,
                Url = item.Url,
                Snippet = item.Content ?? ""
            });
        }

        return results;
    }

    // --- Brave (GET, header X-Subscription-Token) ---

    private async Task<IReadOnlyList<SearchResult>> SearchBraveAsync(
        string query, int maxResults, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return Array.Empty<SearchResult>();

        var url = $"{BraveEndpoint}?q={Uri.EscapeDataString(query)}&count={maxResults}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("X-Subscription-Token", apiKey.Trim());
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        BraveResponse? body;
        try
        {
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Array.Empty<SearchResult>();
            body = await resp.Content.ReadFromJsonAsync<BraveResponse>(JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return Array.Empty<SearchResult>();
        }

        if (body?.Web?.Results is null)
            return Array.Empty<SearchResult>();

        var results = new List<SearchResult>();
        foreach (var item in body.Web.Results)
        {
            if (results.Count >= maxResults)
                break;
            if (string.IsNullOrWhiteSpace(item.Url))
                continue;
            results.Add(new SearchResult
            {
                Title = item.Title ?? item.Url,
                Url = item.Url,
                Snippet = item.Description ?? ""
            });
        }

        return results;
    }

    // --- Tavily (POST JSON {api_key, query, max_results}) ---

    private async Task<IReadOnlyList<SearchResult>> SearchTavilyAsync(
        string query, int maxResults, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return Array.Empty<SearchResult>();

        var payload = new TavilyRequest
        {
            ApiKey = apiKey.Trim(),
            Query = query,
            MaxResults = maxResults
        };

        TavilyResponse? body;
        try
        {
            using var resp = await _http.PostAsJsonAsync(TavilyEndpoint, payload, JsonOptions, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Array.Empty<SearchResult>();
            body = await resp.Content.ReadFromJsonAsync<TavilyResponse>(JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return Array.Empty<SearchResult>();
        }

        if (body?.Results is null)
            return Array.Empty<SearchResult>();

        var results = new List<SearchResult>();
        foreach (var item in body.Results)
        {
            if (results.Count >= maxResults)
                break;
            if (string.IsNullOrWhiteSpace(item.Url))
                continue;
            results.Add(new SearchResult
            {
                Title = item.Title ?? item.Url,
                Url = item.Url,
                Snippet = item.Content ?? ""
            });
        }

        return results;
    }

    // --- Google Programmable Search (GET, key + cx) ---

    private async Task<IReadOnlyList<SearchResult>> SearchGoogleAsync(
        string query, int maxResults, string apiKey, string searchEngineId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(searchEngineId))
            return Array.Empty<SearchResult>();

        // The Custom Search API caps "num" at 10.
        var num = Math.Clamp(maxResults, 1, 10);
        var url = $"{GoogleEndpoint}?key={Uri.EscapeDataString(apiKey.Trim())}" +
                  $"&cx={Uri.EscapeDataString(searchEngineId.Trim())}" +
                  $"&q={Uri.EscapeDataString(query)}&num={num}";

        GoogleResponse? body;
        try
        {
            body = await _http.GetFromJsonAsync<GoogleResponse>(url, JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return Array.Empty<SearchResult>();
        }

        if (body?.Items is null)
            return Array.Empty<SearchResult>();

        var results = new List<SearchResult>();
        foreach (var item in body.Items)
        {
            if (results.Count >= maxResults)
                break;
            if (string.IsNullOrWhiteSpace(item.Link))
                continue;
            results.Add(new SearchResult
            {
                Title = item.Title ?? item.Link,
                Url = item.Link,
                Snippet = item.Snippet ?? ""
            });
        }

        return results;
    }

    public async Task<string> FetchReadableTextAsync(
        string url, int maxChars, CancellationToken ct = default)
    {
        string html;
        try
        {
            html = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ""; // unreachable / slow pages are skipped, not fatal to a research run
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Drop non-content nodes before extracting text.
        var noise = doc.DocumentNode.SelectNodes(
            "//script|//style|//noscript|//nav|//header|//footer|//svg|//form|//aside");
        if (noise is not null)
            foreach (var node in noise)
                node.Remove();

        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        var text = WebUtility.HtmlDecode(body.InnerText);
        text = CollapseWhitespace(text);

        return text.Length > maxChars ? text[..maxChars] : text;
    }

    /// <summary>
    /// DuckDuckGo wraps result links as <c>//duckduckgo.com/l/?uddg=&lt;encoded-target&gt;</c>.
    /// Unwrap that to the real destination; pass through anything already absolute.
    /// </summary>
    private static string NormalizeUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return "";

        if (href.StartsWith("//"))
            href = "https:" + href;

        const string marker = "uddg=";
        var idx = href.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var start = idx + marker.Length;
            var end = href.IndexOf('&', start);
            var encoded = end >= 0 ? href[start..end] : href[start..];
            return Uri.UnescapeDataString(encoded);
        }

        return href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : "";
    }

    private static string CollapseWhitespace(string input)
    {
        var sb = new StringBuilder(input.Length);
        var lastWasSpace = false;
        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                    sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    // --- Provider response DTOs (Web-defaults JSON; property names map case-insensitively) ---

    private sealed class SearxngResponse
    {
        public List<SearxngResult>? Results { get; set; }
    }

    private sealed class SearxngResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Content { get; set; }
    }

    private sealed class BraveResponse
    {
        public BraveWeb? Web { get; set; }
    }

    private sealed class BraveWeb
    {
        public List<BraveResult>? Results { get; set; }
    }

    private sealed class BraveResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Description { get; set; }
    }

    private sealed class TavilyRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("query")]
        public string Query { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("max_results")]
        public int MaxResults { get; set; }
    }

    private sealed class TavilyResponse
    {
        public List<TavilyResult>? Results { get; set; }
    }

    private sealed class TavilyResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Content { get; set; }
    }

    private sealed class GoogleResponse
    {
        public List<GoogleItem>? Items { get; set; }
    }

    private sealed class GoogleItem
    {
        public string? Title { get; set; }
        public string? Link { get; set; }
        public string? Snippet { get; set; }
    }
}
