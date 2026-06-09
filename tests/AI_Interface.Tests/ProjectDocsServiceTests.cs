using System;
using System.IO;
using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for <see cref="ProjectDocsService"/> — the project handbook loader that reads
/// <c>&lt;projectDirectory&gt;/.AI/AI_DOCS.md</c>. The single public method <c>Load</c> is pure I/O over a
/// real directory, so these tests use a unique temp directory (cleaned up in <see cref="Dispose"/>) and
/// never touch machine-specific paths. Behaviour asserted: empty for null/whitespace/missing input,
/// trimmed content when present, truncation-with-marker past the internal cap, and never throws.
/// </summary>
public sealed class ProjectDocsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ProjectDocsService _sut = new();

    public ProjectDocsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AI_Interface_DocsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a leaked temp dir must not fail the test run.
        }
    }

    /// <summary>Writes <paramref name="content"/> to <c>&lt;_tempDir&gt;/.AI/AI_DOCS.md</c>.</summary>
    private string WriteHandbook(string content)
    {
        var aiDir = Path.Combine(_tempDir, ".AI");
        Directory.CreateDirectory(aiDir);
        var path = Path.Combine(aiDir, ProjectDocsService.FileName);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_NullOrWhitespaceDir_ReturnsEmpty(string? projectDirectory)
    {
        // Act
        var result = _sut.Load(projectDirectory!);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void Load_NoHandbookFile_ReturnsEmpty()
    {
        // Arrange: a real, existing directory but with no .AI/AI_DOCS.md inside it.

        // Act
        var result = _sut.Load(_tempDir);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void Load_HandbookPresent_ReturnsTrimmedContent()
    {
        // Arrange: leading/trailing whitespace that Load is expected to strip.
        WriteHandbook("  # Project\nrules\n  ");

        // Act
        var result = _sut.Load(_tempDir);

        // Assert
        Assert.Equal("# Project\nrules", result);
    }

    [Fact]
    public void Load_OversizedHandbook_TruncatesWithMarker()
    {
        // Arrange: content well past the internal 24000-char cap (asserted via behaviour, not the const).
        const int maxChars = 24000;
        const string marker = "\n…(AI_DOCS.md truncated)";
        var content = new string('a', 30000);
        WriteHandbook(content);

        // Act
        var result = _sut.Load(_tempDir);

        // Assert: first maxChars chars of the content, then the truncation marker.
        Assert.True(result.Length < content.Length);
        Assert.EndsWith(marker, result);
        Assert.StartsWith(new string('a', maxChars), result);
        Assert.Equal(maxChars + marker.Length, result.Length);
    }

    [Fact]
    public void Load_UnreadableOrMissingDir_DoesNotThrow()
    {
        // Arrange: a path that does not exist (nested under the temp dir, never created).
        var missing = Path.Combine(_tempDir, "does-not-exist", Guid.NewGuid().ToString("N"));

        // Act
        var result = _sut.Load(missing);

        // Assert: best-effort — returns empty, no exception.
        Assert.Equal("", result);
    }
}
