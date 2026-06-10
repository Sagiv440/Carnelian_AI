using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the pure glob helpers on <see cref="ProjectAgentService"/>: <c>GlobToRegex</c> (compiles a
/// glob into an anchored, case-insensitive <see cref="System.Text.RegularExpressions.Regex"/> over a
/// '/'-separated path) and <c>GlobMatchesPath</c> (is the glob path-scoped, or a bare file-name match?). Both
/// are <c>internal static</c> and I/O-free — deterministic over plain strings, reachable from the test
/// assembly via <c>[assembly: InternalsVisibleTo("AI_Interface.Tests")]</c>. These exercise only the
/// regex/string semantics (asserted via <c>Assert.Matches</c>/<c>Assert.DoesNotMatch</c>, the xUnit-idiomatic
/// equivalents of <c>Regex.IsMatch</c>); the SearchFiles/FindFiles file-walk that consumes them needs a real
/// project dir and is out of scope here.
/// </summary>
public sealed class GlobToRegexTests
{
    // ---- GlobToRegex: * stays within a segment --------------------------------------------------

    [Fact]
    public void GlobToRegex_Star_MatchesWithinSegment_NotAcrossSeparator()
    {
        var regex = ProjectAgentService.GlobToRegex("*.cs");

        // Assert.Matches/DoesNotMatch are the analyzer-approved equivalents of regex.IsMatch(...).
        Assert.Matches(regex, "foo.cs");        // a single segment matches
        Assert.DoesNotMatch(regex, "a/foo.cs"); // '*' is [^/]* so it can't cross the '/'
    }

    // ---- GlobToRegex: ** crosses segments (and "**/" matches zero segments) ---------------------

    [Fact]
    public void GlobToRegex_DoubleStarSlash_MatchesAcrossSegmentsAndAtRoot()
    {
        var regex = ProjectAgentService.GlobToRegex("**/*.cs");

        Assert.Matches(regex, "foo.cs");      // "**/" also matches zero leading segments
        Assert.Matches(regex, "a/b/foo.cs");  // ...and any number of leading segments
    }

    [Fact]
    public void GlobToRegex_DoubleStarInMiddle_ScopesToLeadingSegment()
    {
        var regex = ProjectAgentService.GlobToRegex("src/**/*.ts");

        Assert.Matches(regex, "src/foo.ts");      // zero intermediate segments
        Assert.Matches(regex, "src/a/b.ts");      // ...or several
        Assert.DoesNotMatch(regex, "lib/foo.ts"); // a different leading segment doesn't match
    }

    // ---- GlobToRegex: ? is exactly one non-separator char ---------------------------------------

    [Fact]
    public void GlobToRegex_Question_MatchesExactlyOneNonSeparatorChar()
    {
        var regex = ProjectAgentService.GlobToRegex("foo?.txt");

        Assert.Matches(regex, "fooX.txt");       // '?' consumes the single 'X'
        Assert.DoesNotMatch(regex, "foo.txt");   // '?' requires one char (none present)
        Assert.DoesNotMatch(regex, "foo/.txt");  // '?' is [^/] so it won't match '/'
    }

    // ---- GlobToRegex: regex metacharacters are escaped to literals ------------------------------

    [Fact]
    public void GlobToRegex_RegexMetacharacters_AreEscapedToLiterals()
    {
        var regex = ProjectAgentService.GlobToRegex("a.b+c.cs");

        Assert.Matches(regex, "a.b+c.cs");       // the literal string matches
        Assert.DoesNotMatch(regex, "axbxc.cs");  // proves '.' and '+' were escaped (not regex wildcards)
    }

    // ---- GlobToRegex: case-insensitive ---------------------------------------------------------

    [Fact]
    public void GlobToRegex_IsCaseInsensitive()
    {
        var regex = ProjectAgentService.GlobToRegex("*.CS");
        Assert.Matches(regex, "foo.cs");      // RegexOptions.IgnoreCase
    }

    // ---- GlobToRegex: anchored (^$) ------------------------------------------------------------

    [Fact]
    public void GlobToRegex_EmptyGlob_MatchesOnlyEmptyString()
    {
        var regex = ProjectAgentService.GlobToRegex("");

        Assert.Matches(regex, "");        // anchored "^$"
        Assert.DoesNotMatch(regex, "x");  // anything non-empty fails the anchors
    }

    [Fact]
    public void GlobToRegex_NullGlob_CoalescesToEmpty_MatchesOnlyEmptyString()
    {
        // The impl coalesces null to "" before building the pattern, so it behaves like the empty glob.
        var regex = ProjectAgentService.GlobToRegex(null!);

        Assert.Matches(regex, "");
        Assert.DoesNotMatch(regex, "x");
    }

    // ---- GlobMatchesPath: is the glob path-scoped? ---------------------------------------------

    [Theory]
    [InlineData("*.cs", false)]        // no separator, no '**' -> a bare file-name match
    [InlineData("file?.txt", false)]   // '?' alone is not path-scoping
    [InlineData("src/*.cs", true)]     // contains '/'
    [InlineData("**/*.cs", true)]      // contains '**'
    [InlineData("a\\b", true)]         // contains a backslash separator
    public void GlobMatchesPath_DetectsPathScopedGlobs(string glob, bool expected)
    {
        Assert.Equal(expected, ProjectAgentService.GlobMatchesPath(glob));
    }
}
