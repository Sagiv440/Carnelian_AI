using System;
using System.IO;
using AI_Interface.Models;
using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the security-critical handbook path guards on <see cref="ProjectAgentService"/>:
/// <c>IsHandbookPath</c> (refuse writes/deletes that target <c>.AI/AI_DOCS.md</c> so <c>update_docs</c> stays
/// its sole writer) and <c>ContainsHandbook</c> (refuse deleting a folder that holds the handbook). Both are
/// <c>internal static bool (Project, string)</c> doing pure <see cref="Path.GetFullPath"/> normalization — no
/// disk access — so a fixed absolute project directory is enough. The real callers pass already-resolved
/// absolute paths (after <c>TryResolve</c>), which these tests reproduce via
/// <c>Path.GetFullPath(Path.Combine(project.Directory, relative))</c>. Path comparison is OS-dependent
/// (case-insensitive on Windows, case-sensitive elsewhere), so the case-variant assertion is Windows-only.
/// </summary>
public sealed class HandbookGuardTests
{
    // A real absolute base (not created on disk — the helpers never touch the filesystem) so GetFullPath is
    // deterministic and cross-platform. Each test resolves inputs relative to this directory.
    private static readonly Project Project =
        new("proj", Path.Combine(Path.GetTempPath(), "AI_Interface_HandbookGuardTests_proj"));

    /// <summary>Resolves <paramref name="relative"/> the way the real callers do (post-TryResolve).</summary>
    private static string Resolve(string relative) =>
        Path.GetFullPath(Path.Combine(Project.Directory, relative));

    // ---- IsHandbookPath: true for the handbook reached via equivalent paths --------------------

    [Theory]
    [InlineData(".AI/AI_DOCS.md")]
    [InlineData("./.AI/AI_DOCS.md")]
    [InlineData(".AI/sub/../AI_DOCS.md")]
    public void IsHandbookPath_EquivalentPaths_ReturnsTrue(string relative)
    {
        // Arrange
        var full = Resolve(relative);

        // Act / Assert
        Assert.True(ProjectAgentService.IsHandbookPath(Project, full));
    }

    [Fact]
    public void IsHandbookPath_BackslashForm_ReturnsTrue()
    {
        // Arrange: a backslash-separated relative path. On Windows this is a separator; Path.Combine +
        // GetFullPath normalizes it to the same handbook path.
        var full = Resolve(@".AI\AI_DOCS.md");

        // Act / Assert
        Assert.True(ProjectAgentService.IsHandbookPath(Project, full));
    }

    [Fact]
    public void IsHandbookPath_CaseVariant_IsTrueOnlyOnWindows()
    {
        // Arrange: a differently-cased path. The comparison is OrdinalIgnoreCase on Windows and Ordinal
        // elsewhere, so only assert the case-insensitive expectation under Windows.
        if (!OperatingSystem.IsWindows())
            return; // Case-sensitive filesystems would (correctly) not match; not asserted off Windows.

        var full = Resolve(".ai/ai_docs.md");

        // Act / Assert
        Assert.True(ProjectAgentService.IsHandbookPath(Project, full));
    }

    // ---- IsHandbookPath: false for near-misses -------------------------------------------------

    [Theory]
    [InlineData(".AI/AI_DOCS.md.bak")] // a backup, not the handbook
    [InlineData("AI_DOCS.md")]         // handbook name at the project root, not under .AI
    [InlineData(".AI/other.md")]       // a different file in .AI
    [InlineData(".AI")]                // the .AI folder itself, not the file
    public void IsHandbookPath_NearMisses_ReturnsFalse(string relative)
    {
        // Arrange
        var full = Resolve(relative);

        // Act / Assert
        Assert.False(ProjectAgentService.IsHandbookPath(Project, full));
    }

    // ---- ContainsHandbook: true for folders that hold the handbook -----------------------------

    [Fact]
    public void ContainsHandbook_AiFolder_ReturnsTrue()
    {
        // Arrange: the .AI folder directly contains the handbook.
        var full = Resolve(".AI");

        // Act / Assert
        Assert.True(ProjectAgentService.ContainsHandbook(Project, full));
    }

    [Fact]
    public void ContainsHandbook_ProjectRoot_ReturnsTrue()
    {
        // Arrange: the project root is an ancestor of .AI/AI_DOCS.md.
        var full = Path.GetFullPath(Project.Directory);

        // Act / Assert
        Assert.True(ProjectAgentService.ContainsHandbook(Project, full));
    }

    // ---- ContainsHandbook: false otherwise -----------------------------------------------------

    [Theory]
    [InlineData(".AI/skills")]     // a sibling subfolder under .AI, does not contain the handbook
    [InlineData(".AI/AI_DOCS.md")] // the handbook file itself contains nothing
    [InlineData("src")]            // an unrelated folder
    public void ContainsHandbook_NonContainingPaths_ReturnsFalse(string relative)
    {
        // Arrange
        var full = Resolve(relative);

        // Act / Assert
        Assert.False(ProjectAgentService.ContainsHandbook(Project, full));
    }
}
