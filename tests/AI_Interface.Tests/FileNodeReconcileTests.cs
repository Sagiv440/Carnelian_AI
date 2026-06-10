using System;
using System.IO;
using System.Linq;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Tests for <see cref="FileNode.Reconcile"/> — the in-place merge that keeps the project Files tree live
/// without a full rebuild. The interesting behaviour is structural (folders-first/alphabetical ordering,
/// add/remove, and — crucially — that existing nodes survive so an expanded sub-tree isn't collapsed), so
/// these drive a throwaway temp directory rather than mocking the filesystem. Each test cleans up after
/// itself via <see cref="IDisposable"/>.
/// </summary>
public sealed class FileNodeReconcileTests : IDisposable
{
    private readonly string _root;

    public FileNodeReconcileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ainode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private string Dir(string name)
    {
        var p = Path.Combine(_root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    private void File_(string relative, string content = "x") =>
        File.WriteAllText(Path.Combine(_root, relative), content);

    private static string[] Names(FileNode node) => node.Children.Select(c => c.Name).ToArray();

    [Fact]
    public void Reconcile_AddsRemovesAndKeeps_DirsFirstThenAlphabetical()
    {
        Dir("aaa");
        File_("bbb.txt");
        File_("ccc.txt");

        var root = new FileNode(_root, isDirectory: true);
        root.Load();
        Assert.Equal(new[] { "aaa", "bbb.txt", "ccc.txt" }, Names(root));
        var aaaBefore = root.Children.Single(c => c.Name == "aaa");

        // Disk changes: drop a file, add a file and a folder.
        File.Delete(Path.Combine(_root, "ccc.txt"));
        File_("ddd.txt");
        Dir("zzz");

        root.Reconcile();

        // Directories (aaa, zzz) first, then files (bbb.txt, ddd.txt) — all alphabetical; ccc.txt gone.
        Assert.Equal(new[] { "aaa", "zzz", "bbb.txt", "ddd.txt" }, Names(root));
        // The untouched "aaa" node is the SAME instance — not recreated.
        Assert.Same(aaaBefore, root.Children.Single(c => c.Name == "aaa"));
    }

    [Fact]
    public void Reconcile_PreservesExpandedSubtree()
    {
        var sub = Dir("aaa");
        File.WriteAllText(Path.Combine(sub, "inner.txt"), "y");
        File_("bbb.txt");

        var root = new FileNode(_root, isDirectory: true);
        root.Load();
        var aaa = root.Children.Single(c => c.Name == "aaa");
        aaa.IsExpanded = true;             // loads + expands the sub-folder
        Assert.Contains(aaa.Children, c => c.Name == "inner.txt");

        // A sibling change at the root must not disturb the expanded "aaa" sub-tree.
        File_("ccc.txt");
        root.Reconcile();

        var aaaAfter = root.Children.Single(c => c.Name == "aaa");
        Assert.Same(aaa, aaaAfter);
        Assert.True(aaaAfter.IsExpanded);
        Assert.Contains(aaaAfter.Children, c => c.Name == "inner.txt");
        Assert.Contains(root.Children, c => c.Name == "ccc.txt");
    }

    [Fact]
    public void Reconcile_MatchesAFreshLoad_AfterMixedChanges()
    {
        Dir("alpha");
        Dir("gamma");
        File_("one.txt");
        File_("two.txt");

        var reconciled = new FileNode(_root, isDirectory: true);
        reconciled.Load();

        // A mix of additions and removals across both categories.
        Directory.Delete(Path.Combine(_root, "gamma"), recursive: true);
        File.Delete(Path.Combine(_root, "one.txt"));
        Dir("beta");
        File_("three.txt");

        reconciled.Reconcile();

        // The reconciled view must be byte-for-byte the same order/content as a from-scratch load.
        var fresh = new FileNode(_root, isDirectory: true);
        fresh.Load();
        Assert.Equal(Names(fresh), Names(reconciled));
        Assert.Equal(new[] { "alpha", "beta", "three.txt", "two.txt" }, Names(reconciled));
    }

    [Fact]
    public void Reconcile_RespectsMaxChildrenCap_LikeLoad()
    {
        // Exceed the 2000-child cap so both Load() and Reconcile() must truncate identically.
        for (var i = 0; i < 2050; i++)
            File_($"f{i:0000}.txt");

        var reconciled = new FileNode(_root, isDirectory: true);
        reconciled.Load();
        Assert.Equal(2000, reconciled.Children.Count);

        File_("f9999.txt"); // one more entry on disk
        reconciled.Reconcile();

        var fresh = new FileNode(_root, isDirectory: true);
        fresh.Load();
        Assert.Equal(2000, reconciled.Children.Count);
        Assert.Equal(Names(fresh), Names(reconciled)); // same truncation point as a fresh load
    }

    [Fact]
    public void Reconcile_IsNoOp_WhenFolderNeverLoaded()
    {
        Dir("aaa"); // exists on disk, but we never expand/load the root node

        var root = new FileNode(_root, isDirectory: true);
        // A fresh directory node carries exactly one (invisible) placeholder child until first load.
        Assert.Single(root.Children);
        Assert.Equal("", root.Children[0].FullPath);

        root.Reconcile(); // must do nothing — it reloads fresh on expand instead

        Assert.Single(root.Children);
        Assert.Equal("", root.Children[0].FullPath);
    }
}
