using System.Text.Json.Nodes;
using ConfigFerry.Core.Configuration;

namespace ConfigFerry.Core.Tests;

public class SettingsTreeBuilderTests
{
    private static JsonObject Build(IDictionary<string, string> settings, List<string>? warnings = null) =>
        SettingsTreeBuilder.Build(settings, warnings ?? []);

    [Fact]
    public void Builds_NestedObjects_FromBothSeparators()
    {
        var tree = Build(new Dictionary<string, string>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging__LogLevel__System"] = "Error",
            ["Name"] = "app",
        });

        Assert.Equal("Warning", (string?)tree["Logging"]!["LogLevel"]!["Default"]);
        Assert.Equal("Error", (string?)tree["Logging"]!["LogLevel"]!["System"]);
        Assert.Equal("app", (string?)tree["Name"]);
    }

    [Fact]
    public void Builds_Arrays_FromConsecutiveIndexes()
    {
        var tree = Build(new Dictionary<string, string>
        {
            ["Hosts__1"] = "b",
            ["Hosts__0"] = "a",
            ["Hosts__2"] = "c",
        });

        var array = Assert.IsType<JsonArray>(tree["Hosts"]);
        Assert.Equal(["a", "b", "c"], array.Select(n => (string?)n));
    }

    [Fact]
    public void NonConsecutiveIndexes_StayAnObject()
    {
        var tree = Build(new Dictionary<string, string> { ["X__0"] = "a", ["X__2"] = "c" });

        Assert.IsType<JsonObject>(tree["X"]);
    }

    [Fact]
    public void Arrays_OfObjects_AreSupported()
    {
        var tree = Build(new Dictionary<string, string>
        {
            ["Endpoints__0__Url"] = "u0",
            ["Endpoints__1__Url"] = "u1",
        });

        var array = Assert.IsType<JsonArray>(tree["Endpoints"]);
        Assert.Equal("u1", (string?)array[1]!["Url"]);
    }

    [Fact]
    public void SameKeyDifferentCasing_IsOneNode()
    {
        var tree = Build(new Dictionary<string, string> { ["A__b"] = "1", ["a__C"] = "2" });

        Assert.Single(tree);
        Assert.Equal(2, ((JsonObject)tree.First().Value!).Count);
    }

    [Fact]
    public void ValueAndSectionConflict_SectionWins_WithWarning()
    {
        var warnings = new List<string>();
        var tree = Build(new Dictionary<string, string> { ["A"] = "x", ["A__B"] = "y" }, warnings);

        Assert.Equal("y", (string?)tree["A"]!["B"]);
        Assert.Single(warnings);
    }
}
