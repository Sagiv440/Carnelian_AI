using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the pure activity-feed helpers on <see cref="ProjectAgentService"/>: <c>IconFor</c>
/// (tool name → glyph for a "Started" row) and <c>IsFailure</c> (does a tool-result string indicate the
/// call didn't succeed?). Both are <c>internal static</c> string-only functions with no I/O — deterministic
/// in isolation. Widened from <c>private static</c> to <c>internal static</c> for testing (no logic change),
/// mirroring how <c>IsHandbookPath</c>/<c>ContainsHandbook</c> are exposed via <c>InternalsVisibleTo</c>.
/// </summary>
public sealed class ProjectAgentActivityHelpersTests
{
    // ---- IconFor: tool name -> glyph -----------------------------------------------------------

    [Theory]
    [InlineData("list_directory", "📂")]
    [InlineData("read_file", "📄")]
    [InlineData("write_file", "✏️")]
    [InlineData("create_folder", "📁")]
    [InlineData("delete_file", "🗑")]
    [InlineData("delete_folder", "🗑")]
    [InlineData("run_command", "⌘")]
    [InlineData("install_software", "📦")]
    [InlineData("remember", "💾")]
    [InlineData("create_skill", "✨")]
    [InlineData("update_docs", "📘")]
    public void IconFor_KnownTool_ReturnsExpectedGlyph(string tool, string expected)
    {
        Assert.Equal(expected, ProjectAgentService.IconFor(tool));
    }

    [Theory]
    [InlineData("unknown_tool")]
    [InlineData("")]
    [InlineData("READ_FILE")] // the switch is case-sensitive -> not a known name
    public void IconFor_UnknownTool_ReturnsWrenchFallback(string tool)
    {
        Assert.Equal("🔧", ProjectAgentService.IconFor(tool));
    }

    // ---- IsFailure: failure markers -> true ----------------------------------------------------

    [Theory]
    [InlineData("Error: something went wrong")]                                   // "Error:" prefix
    [InlineData("error: lowercase prefix still matches (OrdinalIgnoreCase)")]      // case-insensitive prefix
    [InlineData("The user declined to run this action.")]                          // "declined to run"
    [InlineData("This agent is not permitted to write files.")]                    // "not permitted"
    [InlineData("Software installation is disabled. Ask the user to allow installs.")] // "is disabled"
    [InlineData("Refusing to delete the project root directory itself.")]          // "Refusing to"
    [InlineData("The project handbook (.AI/AI_DOCS.md) can only be changed with the update_docs tool.")] // handbook guard
    [InlineData("File not found: src/App.jsx")]                                    // "not found:"
    [InlineData("Failed to start command: npm")]                                   // "Failed to start command"
    [InlineData("Command timed out after 120s and was terminated.")]               // "timed out after"
    [InlineData("Path 'x' is outside the project directory and was blocked.")]     // "was blocked"
    public void IsFailure_FailureMarkers_ReturnTrue(string result)
    {
        Assert.True(ProjectAgentService.IsFailure(result));
    }

    [Theory]
    [InlineData("Wrote 42 characters to App.jsx.")]
    [InlineData("read 120 lines")]
    [InlineData("Remembered (this project): the build uses vite.")]
    public void IsFailure_SuccessString_ReturnsFalse(string result)
    {
        Assert.False(ProjectAgentService.IsFailure(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void IsFailure_NullOrEmpty_ReturnsFalse(string? result)
    {
        // Guarded by string.IsNullOrEmpty at the top of the method.
        Assert.False(ProjectAgentService.IsFailure(result!));
    }

    // ---- CurrentActionLabel: live "current action" status-bar string ---------------------------
    // Pure/deterministic: glyph (from IconFor) + summary, plus a single-line, length-capped detail when
    // present. No I/O. The icon prefix is asserted via IconFor's known mappings.

    [Theory]
    [InlineData("read_file", "Read file", "📄 Read file")]
    [InlineData("list_directory", "List directory", "📂 List directory")]
    [InlineData("write_file", "Write file", "✏️ Write file")]
    [InlineData("unknown_tool", "Do a thing", "🔧 Do a thing")]
    public void CurrentActionLabel_EmptyDetail_OmitsSeparator(string tool, string summary, string expected)
    {
        // Empty detail -> "{icon} {summary}" with NO " · " separator.
        Assert.Equal(expected, ProjectAgentService.CurrentActionLabel(tool, summary, ""));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData(null)]
    public void CurrentActionLabel_WhitespaceOrNullDetail_OmitsSeparator(string? detail)
    {
        // Whitespace/null collapses to empty after Replace+Trim -> no separator (same as empty detail).
        Assert.Equal("📄 Read file", ProjectAgentService.CurrentActionLabel("read_file", "Read file", detail!));
    }

    [Fact]
    public void CurrentActionLabel_NonEmptyDetail_AppendsWithMiddotSeparator()
    {
        var label = ProjectAgentService.CurrentActionLabel("run_command", "Run command", "npm run build");
        Assert.Equal("⌘ Run command · npm run build", label);
    }

    [Fact]
    public void CurrentActionLabel_MultiLineDetail_CollapsesToOneTrimmedLine()
    {
        // \r and \n in the detail become spaces and the whole thing is trimmed (a multi-line command
        // collapses onto one status line). The interior newline-turned-space stays a single space here
        // since the input has exactly one separator between the two parts.
        var label = ProjectAgentService.CurrentActionLabel("run_command", "Run command", "  echo hi\r\n");
        Assert.Equal("⌘ Run command · echo hi", label);
    }

    [Fact]
    public void CurrentActionLabel_MultiLineDetail_EachLineBreakBecomesSpace()
    {
        // Two line breaks inside the body become two spaces (Replace is per-char, no whitespace collapsing).
        var label = ProjectAgentService.CurrentActionLabel("run_command", "Run command", "a\nb\nc");
        Assert.Equal("⌘ Run command · a b c", label);
    }

    [Fact]
    public void CurrentActionLabel_LongDetail_TruncatedTo80CharsPlusOneEllipsis()
    {
        // A 200-char detail is capped to exactly the first 80 chars + a single ellipsis character.
        var detail = new string('x', 200);
        var label = ProjectAgentService.CurrentActionLabel("run_command", "Run command", detail);

        const string prefix = "⌘ Run command · ";
        Assert.StartsWith(prefix, label);

        var shown = label[prefix.Length..];
        Assert.Equal(81, shown.Length);          // 80 chars + 1 ellipsis char
        Assert.EndsWith("…", shown);
        Assert.Equal(new string('x', 80) + "…", shown);
    }

    [Fact]
    public void CurrentActionLabel_DetailExactly80Chars_NotTruncated()
    {
        // The cap triggers only when length > 80, so exactly 80 is shown verbatim (no ellipsis).
        var detail = new string('y', 80);
        var label = ProjectAgentService.CurrentActionLabel("run_command", "Run command", detail);

        Assert.Equal("⌘ Run command · " + detail, label);
        Assert.DoesNotContain("…", label);
    }
}
