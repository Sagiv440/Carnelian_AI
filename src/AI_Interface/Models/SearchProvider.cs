namespace AI_Interface.Models;

/// <summary>
/// Web search backend. <see cref="DuckDuckGo"/> is keyless (the default); the others each
/// require credentials configured in Settings → Web Search.
/// </summary>
public enum SearchProvider
{
    /// <summary>Keyless DuckDuckGo HTML scraping. No configuration required.</summary>
    DuckDuckGo,

    /// <summary>Self-hosted SearXNG instance. Requires the instance URL.</summary>
    SearXNG,

    /// <summary>Brave Search API. Requires an API key.</summary>
    Brave,

    /// <summary>Tavily Search API. Requires an API key.</summary>
    Tavily,

    /// <summary>Google Programmable Search (Custom Search JSON API). Requires an API key + engine ID.</summary>
    Google
}

/// <summary>Friendly display names for <see cref="SearchProvider"/> values.</summary>
public static class SearchProviderExtensions
{
    /// <summary>Human-readable label shown in the provider selector.</summary>
    public static string DisplayName(this SearchProvider provider) => provider switch
    {
        SearchProvider.DuckDuckGo => "DuckDuckGo (no key)",
        SearchProvider.SearXNG => "SearXNG (self-hosted)",
        SearchProvider.Brave => "Brave Search",
        SearchProvider.Tavily => "Tavily",
        SearchProvider.Google => "Google Programmable Search",
        _ => provider.ToString()
    };
}
