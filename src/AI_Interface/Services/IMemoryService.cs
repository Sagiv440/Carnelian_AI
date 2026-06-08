using System.Collections.Generic;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Persistent memory: durable facts the assistant recalls across sessions. Two scopes — <b>global</b>
/// (about the user, in app-data) and <b>project</b> (about the active project, under <c>.AI/memory.md</c>).
/// Both stores are portable Markdown bullet lists (see <see cref="MemoryMarkdown"/>); every read/write is
/// best-effort (a failed file op must never crash the app).
/// </summary>
public interface IMemoryService
{
    /// <summary>All remembered facts in a scope (project scope needs <paramref name="projectDir"/>).</summary>
    IReadOnlyList<MemoryEntry> Load(MemoryScope scope, string? projectDir);

    /// <summary>Appends a fact; ignored if blank or an exact (case-insensitive) duplicate of an existing one.</summary>
    void Add(MemoryScope scope, string text, string source, string? projectDir);

    /// <summary>Removes the first fact whose text matches (case-insensitive). No-op if not found.</summary>
    void Remove(MemoryScope scope, string text, string? projectDir);

    /// <summary>Forgets every fact in a scope.</summary>
    void Clear(MemoryScope scope, string? projectDir);

    /// <summary>
    /// A compact "what you remember" block for the system prompt (global + project facts), or "" when
    /// there's nothing to recall. The caller decides whether memory is enabled before calling this.
    /// </summary>
    string BuildContextBlock(string? projectDir);
}
