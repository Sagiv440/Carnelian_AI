using System;
using System.Linq;
using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the pure, I/O-free OS-decision helpers extracted from <see cref="OllamaInstaller"/>.
/// These deliberately avoid <c>InstallAsync</c> (which launches real processes / network); they only
/// exercise the parameterised seam (<see cref="OSKind"/>) so every OS branch is asserted on any host.
/// </summary>
public class OllamaInstallerTests
{
    // --- InstallSourceUrl ----------------------------------------------------------------------

    // NOTE: tests with an OSKind (internal enum) parameter are themselves declared `internal`.
    // A public method cannot expose a less-accessible parameter type (CS0051); xUnit happily
    // discovers and runs internal test methods.

    [Theory]
    [InlineData(OSKind.Windows, "https://ollama.com/download/OllamaSetup.exe")]
    [InlineData(OSKind.Linux, "https://ollama.com/install.sh")]
    internal void InstallSourceUrl_SupportedOs_ReturnsOfficialEndpoint(OSKind os, string expected)
    {
        // Act
        var url = OllamaInstaller.InstallSourceUrl(os);

        // Assert
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData(OSKind.MacOS)]
    [InlineData(OSKind.Other)]
    internal void InstallSourceUrl_UnsupportedOs_ReturnsNull(OSKind os)
    {
        // Act
        var url = OllamaInstaller.InstallSourceUrl(os);

        // Assert
        Assert.Null(url);
    }

    // --- CandidateExecutablePaths --------------------------------------------------------------

    [Theory]
    [InlineData(OSKind.Windows)]
    [InlineData(OSKind.Linux)]
    [InlineData(OSKind.MacOS)]
    internal void CandidateExecutablePaths_KnownOs_IsNonEmpty(OSKind os)
    {
        // Act
        var paths = OllamaInstaller.CandidateExecutablePaths(os);

        // Assert
        Assert.NotNull(paths);
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.False(string.IsNullOrWhiteSpace(p)));
    }

    [Fact]
    public void CandidateExecutablePaths_Windows_AllEndWithOllamaExe()
    {
        // Act
        var paths = OllamaInstaller.CandidateExecutablePaths(OSKind.Windows);

        // Assert
        Assert.All(paths, p => Assert.EndsWith("ollama.exe", p, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(OSKind.Linux)]
    [InlineData(OSKind.MacOS)]
    [InlineData(OSKind.Other)] // the helper treats any non-Windows/macOS value as Unix-style
    internal void CandidateExecutablePaths_NonWindows_AllEndWithBareOllama(OSKind os)
    {
        // Act
        var paths = OllamaInstaller.CandidateExecutablePaths(os);

        // Assert: bare "ollama" (no .exe) on Unix-style platforms
        Assert.All(paths, p =>
        {
            Assert.EndsWith("ollama", p, StringComparison.Ordinal);
            Assert.DoesNotContain(".exe", p, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void CandidateExecutablePaths_Windows_IncludesLocalAppDataProgramsOllama()
    {
        // Arrange: the well-known per-user install location the official installer uses,
        // i.e. %LOCALAPPDATA%\Programs\Ollama\ollama.exe.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expected = System.IO.Path.Combine(local, "Programs", "Ollama", "ollama.exe");

        // Act
        var paths = OllamaInstaller.CandidateExecutablePaths(OSKind.Windows);

        // Assert
        Assert.Contains(expected, paths);
    }

    [Fact]
    public void CandidateExecutablePaths_Linux_IncludesUsrLocalBin()
    {
        // Act
        var paths = OllamaInstaller.CandidateExecutablePaths(OSKind.Linux);

        // Assert: the official install.sh script lands the binary in /usr/local/bin.
        Assert.Contains("/usr/local/bin/ollama", paths);
    }

    [Fact]
    public void CandidateExecutablePaths_MacOS_IncludesHomebrewLocation()
    {
        // Act
        var paths = OllamaInstaller.CandidateExecutablePaths(OSKind.MacOS);

        // Assert: Apple-silicon Homebrew prefix is a known macOS install spot.
        Assert.Contains("/opt/homebrew/bin/ollama", paths);
    }

    // --- CurrentOs -----------------------------------------------------------------------------

    [Fact]
    public void CurrentOs_ReturnsValueMatchingHost()
    {
        // Arrange: compute the expected OSKind for whatever host the test runs on, so the
        // assertion is correct on Windows, Linux, or macOS CI agents.
        var expected =
            OperatingSystem.IsWindows() ? OSKind.Windows :
            OperatingSystem.IsLinux() ? OSKind.Linux :
            OperatingSystem.IsMacOS() ? OSKind.MacOS :
            OSKind.Other;

        // Act
        var actual = OllamaInstaller.CurrentOs();

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CurrentOs_IsConsistentWithInstallSupport()
    {
        // The host's resolved OS should agree with InstallSourceUrl about whether automatic
        // install is supported (URL present) vs. guided-only (null).
        var os = OllamaInstaller.CurrentOs();
        var url = OllamaInstaller.InstallSourceUrl(os);

        if (os is OSKind.Windows or OSKind.Linux)
            Assert.NotNull(url);
        else
            Assert.Null(url);
    }
}
