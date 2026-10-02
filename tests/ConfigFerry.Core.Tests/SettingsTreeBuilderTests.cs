using System.Text.Json.Nodes;
using ConfigFerry.Core.Configuration;

namespace ConfigFerry.Core.Tests;

[TestClass]
public class SettingsTreeBuilderTests
{
    private static JsonObject Build(IDictionary<string, string> settings, List<string>? warnings = null) =>
        SettingsTreeBuilder.Build(settings, warnings ?? []);

    [TestMethod]
    public void Builds_NestedObjects_FromBothSeparators()
    {
        var tree = Build(new Dictionary<string, string>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging__LogLevel__System"] = "Error",
            ["Name"] = "app",
        });

        Assert.AreEqual("Warning", (string?)tree["Logging"]!["LogLevel"]!["Default"]);
        Assert.AreEqual("Error", (string?)tree["Logging"]!["LogLevel"]!["System"]);
        Assert.AreEqual("app", (string?)tree["Name"]);
    }

    [TestMethod]
    public void Builds_Arrays_FromConsecutiveIndexes()
    {
        var tree = Build(new Dictionary<string, string>
        {
            ["Hosts__1"] = "b",
            ["Hosts__0"] = "a",
            ["Hosts__2"] = "c",
        });

        var array = Assert.IsInstanceOfType<JsonArray>(tree["Hosts"]);
        Assert.AreSequenceEqual(["a", "b", "c"], array.Select(n => (string?)n));
    }

    [TestMethod]
    public void NonConsecutiveIndexes_StayAnObject()
    {
        var tree = Build(new Dictionary<string, string> { ["X__0"] = "a", ["X__2"] = "c" });

        Assert.IsInstanceOfType<JsonObject>(tree["X"]);
    }

    [TestMethod]
    public void Arrays_OfObjects_AreSupported()
    {
        var tree = Build(new Dictionary<string, string>
        {
            ["Endpoints__0__Url"] = "u0",
            ["Endpoints__1__Url"] = "u1",
        });

        var array = Assert.IsInstanceOfType<JsonArray>(tree["Endpoints"]);
        Assert.AreEqual("u1", (string?)array[1]!["Url"]);
    }

    [TestMethod]
    public void SameKeyDifferentCasing_IsOneNode()
    {
        var tree = Build(new Dictionary<string, string> { ["A__b"] = "1", ["a__C"] = "2" });

        Assert.ContainsSingle(tree);
        Assert.AreEqual(2, ((JsonObject)tree.First().Value!).Count);
    }

    [TestMethod]
    public void ValueAndSectionConflict_SectionWins_WithWarning()
    {
        var warnings = new List<string>();
        var tree = Build(new Dictionary<string, string> { ["A"] = "x", ["A__B"] = "y" }, warnings);

        Assert.AreEqual("y", (string?)tree["A"]!["B"]);
        Assert.ContainsSingle(warnings);
    }
}
