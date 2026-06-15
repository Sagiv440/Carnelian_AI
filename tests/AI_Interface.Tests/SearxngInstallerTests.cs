using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>Unit tests for the pure helpers of <see cref="SearxngInstaller"/> (the docker arg vector +
/// the generated config). The Docker calls themselves are integration-only and not exercised here.</summary>
public sealed class SearxngInstallerTests
{
    [Fact]
    public void RunArgs_MapsPort_NamesContainer_MountsConfig()
    {
        var args = SearxngInstaller.RunArgs(8888, "/home/u/.config/AI_Interface/searxng");

        Assert.Equal("run", args[0]);
        Assert.Contains("-d", args);
        Assert.Contains(SearxngInstaller.ContainerName, args);
        Assert.Contains("8888:8080", args);
        Assert.Contains("/home/u/.config/AI_Interface/searxng:/etc/searxng", args);
        Assert.Equal(SearxngInstaller.Image, args[^1]); // image is the final arg
    }

    [Fact]
    public void BuildSettingsYaml_EnablesJsonApi_AndDisablesLimiter()
    {
        var yaml = SearxngInstaller.BuildSettingsYaml("deadbeef");

        Assert.Contains("use_default_settings: true", yaml);
        Assert.Contains("secret_key: \"deadbeef\"", yaml);
        Assert.Contains("limiter: false", yaml);
        Assert.Contains("- json", yaml);   // the JSON format the app's ?format=json calls need
        Assert.Contains("- html", yaml);
    }
}
