namespace AI_Interface.Models;

/// <summary>Which persistent-memory store a fact belongs to.</summary>
public enum MemoryScope
{
    /// <summary>Facts about the user, shared across all projects (<c>&lt;app-data&gt;/AI_Interface/memory.md</c>).</summary>
    Global,

    /// <summary>Facts about the active project (<c>&lt;project&gt;/.AI/memory.md</c>).</summary>
    Project
}

/// <summary>
/// One remembered fact. Persisted as a Markdown bullet (the <see cref="Text"/>) with its
/// <see cref="Source"/> + <see cref="CreatedAtIso"/> tucked into a trailing HTML comment, so the file
/// stays human- and tool-portable. See <see cref="Services.MemoryMarkdown"/>.
/// </summary>
public sealed record MemoryEntry(string Text, string Source, string CreatedAtIso);
