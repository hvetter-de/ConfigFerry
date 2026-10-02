using ConfigFerry.Core.Configuration;

namespace ConfigFerry.Core.Tests;

public class ConfigKeyTests
{
    [Theory]
    [InlineData("A:B:C", "A__B__C")]
    [InlineData("A__B", "A__B")]
    [InlineData("A:B__C", "A__B__C")]
    [InlineData("Plain", "Plain")]
    public void Normalize_UsesDoubleUnderscore(string input, string expected) =>
        Assert.Equal(expected, ConfigKey.Normalize(input));

    [Fact]
    public void Split_IgnoresEmptySegments() =>
        Assert.Equal(["A", "B"], ConfigKey.Split("A::B"));

    [Theory]
    [InlineData("WEBSITE_RUN_FROM_PACKAGE", true)]
    [InlineData("website_node_default_version", true)]
    [InlineData("FUNCTIONS_EXTENSION_VERSION", true)]
    [InlineData("SCM_DO_BUILD_DURING_DEPLOYMENT", true)]
    [InlineData("FUNCTIONS_WORKER_RUNTIME", false)]
    [InlineData("AzureWebJobsStorage", false)]
    [InlineData("MyApp:Setting", false)]
    public void PlatformSettings_Detected(string key, bool expected) =>
        Assert.Equal(expected, PlatformSettings.IsPlatformSetting(key));
}
