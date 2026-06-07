using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;
using HtmlAgilityPack;

namespace AI_Interface.Services;

/// <summary>
/// Web search via the DuckDuckGo HTML endpoint (no API key required) plus page text extraction
/// using HtmlAgilityPack. The injected <see cref="HttpClient"/> is configured with a desktop
/// User-Agent in DI so pages serve their normal markup.
/// </summary>
public sealed class WebSearchService : IWebSearchService
{
    private const string SearchEndpoint = "https://html.duckduckgo.com/html/";

    private readonly HttpClient _http;

    public WebSearchService(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken ct = default)
    {
        var url = $"{SearchEndpoint}?q={Uri.EscapeDataString(query)}";
        string html;
        try
        {
            html = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
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
}
