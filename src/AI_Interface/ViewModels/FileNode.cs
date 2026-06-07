using System;
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
}
