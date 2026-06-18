using System.Linq;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the pure helpers behind the Chat 📄 <c>create_document</c> tool:
/// <see cref="MainWindowViewModel.SanitizeFileBase"/> (title → safe file-name base) and
/// <see cref="MainWindowViewModel.CreateDocumentTool"/> (the advertised tool's shape). Both are reachable
/// via <c>[assembly: InternalsVisibleTo("AI_Interface.Tests")]</c>.
/// </summary>
public class CreateDocumentToolTests
{
    [Theory]
    [InlineData("My Resume", "My Resume")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("report.pdf", "report-pdf")]              // dots become dashes (no extension smuggling)
    [InlineData("a/b\\c:d", "a-b-c-d")]                   // path separators / invalid chars stripped
    public void SanitizeFileBase_ProducesSafeBase(string input, string expected)
    {
        Assert.Equal(expected, MainWindowViewModel.SanitizeFileBase(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void SanitizeFileBase_BlankOrAllInvalid_ReturnsEmpty(string input)
    {
        Assert.Equal("", MainWindowViewModel.SanitizeFileBase(input));
    }

    [Fact]
    public void SanitizeFileBase_CapsLength()
    {
        var result = MainWindowViewModel.SanitizeFileBase(new string('x', 200));
        Assert.True(result.Length <= 80);
    }

    [Fact]
    public void CreateDocumentTool_HasExpectedNameAndRequiredArgs()
    {
        var tool = MainWindowViewModel.CreateDocumentTool();

        Assert.Equal("create_document", tool.Name);

        var props = tool.Parameters.GetProperty("properties");
        Assert.True(props.TryGetProperty("format", out _));
        Assert.True(props.TryGetProperty("title", out _));
        Assert.True(props.TryGetProperty("content", out _));

        var required = tool.Parameters.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.Contains("format", required);
        Assert.Contains("title", required);
        Assert.Contains("content", required);
    }
}
