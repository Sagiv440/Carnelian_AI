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
}
