using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI_Interface.Models;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the startup-launcher recent-projects logic:
/// <see cref="MainWindowViewModel.WithRecentProjectAtFront"/> (pure list maintenance — move-to-front, dedupe,
/// cap) and <see cref="MainWindowViewModel.PrunedRecent"/> (drops entries whose folder no longer exists, capped).
/// </summary>
public sealed class RecentProjectsTests
{
    private static RecentProject R(string name, string dir) => new() { Name = name, Directory = dir };

    // ---- WithRecentProjectAtFront (pure) -------------------------------------------------------

    [Fact]
    public void WithRecentProjectAtFront_AddsToFront_AndKeepsOthersInOrder()
    {
        var existing = new List<RecentProject> { R("A", "/a"), R("B", "/b") };

        var result = MainWindowViewModel.WithRecentProjectAtFront(
            existing, new Project("C", "/c"), cap: 12, StringComparison.Ordinal);

        Assert.Equal(new[] { "/c", "/a", "/b" }, result.Select(r => r.Directory).ToArray());
    }

    [Fact]
    public void WithRecentProjectAtFront_DedupesByDirectory_NoDuplicateEntry()
    {
        var existing = new List<RecentProject> { R("A", "/a"), R("B", "/b") };

        // Re-opening /a moves it to the front and removes the old copy (one entry, not two).
        var result = MainWindowViewModel.WithRecentProjectAtFront(
            existing, new Project("A", "/a"), cap: 12, StringComparison.Ordinal);

        Assert.Equal(new[] { "/a", "/b" }, result.Select(r => r.Directory).ToArray());
    }

    [Fact]
    public void WithRecentProjectAtFront_DedupeHonoursComparer()
    {
        var existing = new List<RecentProject> { R("A", "/Project") };

        // Case-insensitive comparer treats "/project" as the same directory → deduped to one.
        var result = MainWindowViewModel.WithRecentProjectAtFront(
            existing, new Project("A", "/project"), cap: 12, StringComparison.OrdinalIgnoreCase);

        Assert.Single(result);
        Assert.Equal("/project", result[0].Directory);
    }

    [Fact]
    public void WithRecentProjectAtFront_CapsTheList()
    {
        var existing = new List<RecentProject> { R("A", "/a"), R("B", "/b"), R("C", "/c") };

        var result = MainWindowViewModel.WithRecentProjectAtFront(
            existing, new Project("D", "/d"), cap: 2, StringComparison.Ordinal);

        Assert.Equal(new[] { "/d", "/a" }, result.Select(r => r.Directory).ToArray());
    }

    [Fact]
    public void WithRecentProjectAtFront_SkipsBlankExistingEntries()
    {
        var existing = new List<RecentProject> { R("blank", "  "), R("A", "/a") };

        var result = MainWindowViewModel.WithRecentProjectAtFront(
            existing, new Project("New", "/new"), cap: 12, StringComparison.Ordinal);

        Assert.Equal(new[] { "/new", "/a" }, result.Select(r => r.Directory).ToArray());
    }

    [Fact]
    public void WithRecentProjectAtFront_CapOfOne_KeepsOnlyTheNewest()
    {
        var existing = new List<RecentProject> { R("A", "/a") };

        var result = MainWindowViewModel.WithRecentProjectAtFront(
            existing, new Project("B", "/b"), cap: 1, StringComparison.Ordinal);

        Assert.Single(result);
        Assert.Equal("/b", result[0].Directory);
    }

    [Fact]
    public void WithRecentProjectAtFront_ToleratesNullElementsInExisting()
    {
        var existing = new List<RecentProject> { null!, R("A", "/a") };

        var result = MainWindowViewModel.WithRecentProjectAtFront(
            existing, new Project("New", "/new"), cap: 12, StringComparison.Ordinal);

        Assert.Equal(new[] { "/new", "/a" }, result.Select(r => r.Directory).ToArray());
    }

    // ---- PrunedRecent (drops missing folders, capped) ------------------------------------------

    [Fact]
    public void PrunedRecent_DropsNonExistentDirectories_KeepsExistingInOrder()
    {
        var realA = Directory.CreateTempSubdirectory("ai_recentA_").FullName;
        var realB = Directory.CreateTempSubdirectory("ai_recentB_").FullName;
        try
        {
            var recents = new List<RecentProject>
            {
                R("A", realA),
                R("ghost", Path.Combine(Path.GetTempPath(), "ai_recent_missing_" + Guid.NewGuid().ToString("N"))),
                R("B", realB),
            };

            var pruned = MainWindowViewModel.PrunedRecent(recents, max: 8);

            Assert.Equal(new[] { realA, realB }, pruned.Select(r => r.Directory).ToArray());
        }
        finally
        {
            try { Directory.Delete(realA); } catch { }
            try { Directory.Delete(realB); } catch { }
        }
    }

    [Fact]
    public void PrunedRecent_RespectsMax()
    {
        var dirs = Enumerable.Range(0, 3)
            .Select(_ => Directory.CreateTempSubdirectory("ai_recentcap_").FullName).ToList();
        try
        {
            var recents = dirs.Select((d, i) => R($"p{i}", d)).ToList();

            var pruned = MainWindowViewModel.PrunedRecent(recents, max: 2);

            Assert.Equal(2, pruned.Count);
            Assert.Equal(dirs.Take(2), pruned.Select(r => r.Directory));
        }
        finally
        {
            foreach (var d in dirs) { try { Directory.Delete(d); } catch { } }
        }
    }

    [Fact]
    public void PrunedRecent_Null_ReturnsEmpty() =>
        Assert.Empty(MainWindowViewModel.PrunedRecent(null, 8));
}
