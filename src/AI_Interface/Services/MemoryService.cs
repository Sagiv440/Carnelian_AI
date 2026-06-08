using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IMemoryService"/>. Stores facts as Markdown (<see cref="MemoryMarkdown"/>): global
/// memory in <c>&lt;app-data&gt;/AI_Interface/memory.md</c> and per-project memory in
/// <c>&lt;projectDir&gt;/.AI/memory.md</c>. All file I/O is best-effort, matching <see cref="SettingsService"/>
/// / <see cref="AgentService"/>.
/// </summary>
public sealed class MemoryService : IMemoryService
{
    private readonly string _globalPath;

    public MemoryService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AI_Interface");
        _globalPath = Path.Combine(appData, "memory" + MemoryMarkdown.Extension);
    }

    public IReadOnlyList<MemoryEntry> Load(MemoryScope scope, string? projectDir)
    {
        var path = PathFor(scope, projectDir);
        if (path is null)
            return Array.Empty<MemoryEntry>();
        try
        {
            return File.Exists(path)
                ? MemoryMarkdown.Parse(File.ReadAllText(path))
                : Array.Empty<MemoryEntry>();
        }
        catch
        {
            return Array.Empty<MemoryEntry>();
        }
    }

    public void Add(MemoryScope scope, string text, string source, string? projectDir)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
            return;

        var entries = Load(scope, projectDir).ToList();
        if (entries.Any(e => string.Equals(e.Text, text, StringComparison.OrdinalIgnoreCase)))
            return; // don't store the same fact twice

        entries.Add(new MemoryEntry(text, source ?? "", Today()));
        Write(scope, projectDir, entries);
    }

    public void Remove(MemoryScope scope, string text, string? projectDir)
    {
        var entries = Load(scope, projectDir).ToList();
        var idx = entries.FindIndex(e => string.Equals(e.Text, text, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return;
        entries.RemoveAt(idx);
        Write(scope, projectDir, entries);
    }

    public void Clear(MemoryScope scope, string? projectDir) =>
        Write(scope, projectDir, new List<MemoryEntry>());

    public string BuildContextBlock(string? projectDir)
    {
        var global = Load(MemoryScope.Global, null);
        var project = string.IsNullOrWhiteSpace(projectDir)
            ? (IReadOnlyList<MemoryEntry>)Array.Empty<MemoryEntry>()
            : Load(MemoryScope.Project, projectDir);

        if (global.Count == 0 && project.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.Append("Persistent memory — facts you've been asked to remember across sessions. " +
                  "Use them when relevant; don't recite them back unless asked.");
        if (global.Count > 0)
        {
            sb.Append("\nAbout the user:");
            foreach (var e in global)
                sb.Append("\n- ").Append(e.Text);
        }
        if (project.Count > 0)
        {
            sb.Append("\nAbout this project:");
            foreach (var e in project)
                sb.Append("\n- ").Append(e.Text);
        }
        return sb.ToString();
    }

    // ---- helpers ----------------------------------------------------------------------------

    /// <summary>The memory file for a scope, or null when project scope is requested without a project.</summary>
    private string? PathFor(MemoryScope scope, string? projectDir)
    {
        if (scope == MemoryScope.Global)
            return _globalPath;
        if (string.IsNullOrWhiteSpace(projectDir))
            return null;
        return Path.Combine(projectDir, ".AI", "memory" + MemoryMarkdown.Extension);
    }

    private void Write(MemoryScope scope, string? projectDir, IReadOnlyList<MemoryEntry> entries)
    {
        var path = PathFor(scope, projectDir);
        if (path is null)
            return;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, MemoryMarkdown.Serialize(entries));
        }
        catch
        {
            // Best-effort: a failed write must not crash the app.
        }
    }

    /// <summary>Today's date as yyyy-MM-dd (local) for the bullet metadata.</summary>
    private static string Today() => DateTime.Now.ToString("yyyy-MM-dd");
}
