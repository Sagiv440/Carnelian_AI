using System.Collections.Generic;

namespace AI_Interface.Models;

/// <summary>One result returned by the Ollama library search, with all metadata available from the search page.</summary>
public sealed record OllamaSearchResult(
    string Name,
    string Description,
    string Pulls,
    string Tags,
    string Updated,
    IReadOnlyList<string> Sizes,
    IReadOnlyList<string> Capabilities);
