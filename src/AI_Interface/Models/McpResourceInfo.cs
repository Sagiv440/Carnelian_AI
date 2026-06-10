namespace AI_Interface.Models;

/// <summary>
/// A resource exposed by an MCP server — readable data (a file, document, row set, …) the user can browse and
/// attach as prompt context. Carries the owning server so <c>McpService</c> can route the read back to it.
/// </summary>
public sealed record McpResourceInfo(
    string ServerId, string ServerName, string Uri, string Name, string Description, string MimeType)
{
    /// <summary>Display label: the resource name, falling back to its URI.</summary>
    public string Label => string.IsNullOrWhiteSpace(Name) ? Uri : Name;
}

/// <summary>A prompt template exposed by an MCP server — surfaced in the composer's slash (/) palette.</summary>
public sealed record McpPromptInfo(string ServerId, string ServerName, string Name, string Description);

/// <summary>A tool exposed by an MCP server (its real name + description), shown in the Settings panel after Test.</summary>
public sealed record McpToolSummary(string Name, string Description);

/// <summary>An MCP resource the user has fetched and staged as context for the next prompt (a composer chip).</summary>
public sealed record McpAttachedResource(string Label, string Text);
