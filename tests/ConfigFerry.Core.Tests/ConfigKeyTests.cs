using ConfigFerry.Core.Configuration;

namespace ConfigFerry.Core.Tests;

[TestClass]
public class ConfigKeyTests
{
    [TestMethod]
    [DataRow("A:B:C", "A__B__C")]
    [DataRow("A__B", "A__B")]
    [DataRow("A:B__C", "A__B__C")]
    [DataRow("Plain", "Plain")]
    public void Normalize_UsesDoubleUnderscore(string input, string expected) =>
        Assert.AreEqual(expected, ConfigKey.Normalize(input));

    [TestMethod]
    public void Split_IgnoresEmptySegments() =>
        Assert.AreSequenceEqual(["A", "B"], ConfigKey.Split("A::B"));

    [TestMethod]
    [DataRow("WEBSITE_RUN_FROM_PACKAGE", true)]
    [DataRow("website_node_default_version", true)]
    [DataRow("FUNCTIONS_EXTENSION_VERSION", true)]
    [DataRow("SCM_DO_BUILD_DURING_DEPLOYMENT", true)]
    [DataRow("FUNCTIONS_WORKER_RUNTIME", false)]
    [DataRow("AzureWebJobsStorage", false)]
    [DataRow("MyApp:Setting", false)]
    public void PlatformSettings_Detected(string key, bool expected) =>
        Assert.AreEqual(expected, PlatformSettings.IsPlatformSetting(key));
}
