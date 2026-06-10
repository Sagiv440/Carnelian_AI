using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Interface.ViewModels;

/// <summary>
/// One node in the project file tree. Directories load their children lazily the first time they are
/// expanded (a placeholder child makes the expand arrow appear while still collapsed), so opening a
/// large project doesn't walk the whole tree up front.
/// </summary>
public sealed partial class FileNode : ViewModelBase
{
    private const int MaxChildren = 2000;

    private bool _loaded;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public string Glyph => IsDirectory ? "📁" : "📄";

    public ObservableCollection<FileNode> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    public FileNode(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;

        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Name = string.IsNullOrEmpty(name) ? fullPath : name;

        if (isDirectory)
            Children.Add(new FileNode()); // placeholder so the expand arrow shows before loading
    }

    // Placeholder leaf (never displayed: it only exists to reveal a directory's expand arrow).
    private FileNode()
    {
        Name = "";
        FullPath = "";
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded && IsDirectory)
            Load();
    }

    /// <summary>(Re)reads this directory's immediate children: folders first, then files, alphabetical.</summary>
    public void Load()
    {
        _loaded = true;
        Children.Clear();
        try
        {
            var count = 0;
            foreach (var dir in Directory.GetDirectories(FullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                Children.Add(new FileNode(dir, isDirectory: true));
                if (++count >= MaxChildren)
                    return;
            }
            foreach (var file in Directory.GetFiles(FullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                Children.Add(new FileNode(file, isDirectory: false));
                if (++count >= MaxChildren)
                    return;
            }
        }
        catch
        {
            // Permission / IO error: leave the folder empty rather than crashing the tree.
        }
    }

    /// <summary>
    /// Re-reads this directory from disk and merges the result into <see cref="Children"/> <b>in place</b>:
    /// vanished entries are removed, new ones inserted in sort order, and existing nodes are kept (so their
    /// expansion state and already-loaded sub-trees survive). No-op for a folder that was never expanded
    /// (it reloads fresh on expand) — which also means changes buried in a collapsed folder are ignored
    /// until it's opened. A rename is seen as a remove + add (the path is the identity), so a renamed
    /// folder reappears collapsed. Must be called on the UI thread (it mutates the bound collection).
    /// </summary>
    public void Reconcile()
    {
        if (!IsDirectory || !_loaded)
            return;

        List<string> dirs, files;
        try
        {
            dirs = Directory.GetDirectories(FullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            files = Directory.GetFiles(FullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return; // permission / IO error — leave the current view untouched
        }

        // Desired order matches Load(): directories first, then files; same overall cap.
        var desired = new List<(string Path, bool IsDir)>(dirs.Count + files.Count);
        foreach (var d in dirs) desired.Add((d, true));
        foreach (var f in files) desired.Add((f, false));
        if (desired.Count > MaxChildren)
            desired = desired.GetRange(0, MaxChildren);

        // Drop children that no longer exist on disk.
        var keep = new HashSet<string>(desired.Select(d => d.Path), StringComparer.Ordinal);
        for (var i = Children.Count - 1; i >= 0; i--)
            if (!keep.Contains(Children[i].FullPath))
                Children.RemoveAt(i);

        // Walk the desired list; keep matches in place, move displaced existing nodes, insert new ones.
        var existing = Children.ToDictionary(c => c.FullPath, c => c, StringComparer.Ordinal);
        for (var i = 0; i < desired.Count; i++)
        {
            var (path, isDir) = desired[i];
            if (i < Children.Count && string.Equals(Children[i].FullPath, path, StringComparison.Ordinal))
                continue;

            if (existing.TryGetValue(path, out var node))
                Children.Move(Children.IndexOf(node), i);
            else
                Children.Insert(i, new FileNode(path, isDir));
        }
    }
}
