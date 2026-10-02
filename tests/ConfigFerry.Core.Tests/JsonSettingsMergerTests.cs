using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigFerry.Core.Configuration;
using ConfigFerry.Core.Models;

namespace ConfigFerry.Core.Tests;

public class JsonSettingsMergerTests
{
    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Azure_Wins_OnScalarConflict_AndLocalOnlyKeysSurvive()
    {
        var local = Obj("""{ "A": "local", "OnlyLocal": "keep" }""");

        var stats = JsonSettingsMerger.Merge(local, Obj("""{ "A": "azure", "OnlyAzure": "new" }"""));

        Assert.Equal("azure", (string?)local["A"]);
        Assert.Equal("keep", (string?)local["OnlyLocal"]);
        Assert.Equal("new", (string?)local["OnlyAzure"]);
        Assert.Equal(new MergeStats(1, 1, 0), stats);
    }

    [Fact]
    public void Nested_Objects_AreMergedDeeply()
    {
        var local = Obj("""{ "Logging": { "LogLevel": { "Default": "Debug", "System": "Info" } } }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "Logging": { "LogLevel": { "Default": "Warning" } } }"""));

        Assert.Equal("Warning", (string?)local["Logging"]!["LogLevel"]!["Default"]);
        Assert.Equal("Info", (string?)local["Logging"]!["LogLevel"]!["System"]);
    }

    [Fact]
    public void PropertyNames_MatchCaseInsensitively_KeepingLocalCasing()
    {
        var local = Obj("""{ "connectionStrings": { "Db": "old" } }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "ConnectionStrings": { "DB": "new" } }"""));

        Assert.Single(local);
        Assert.Equal("new", (string?)local["connectionStrings"]!["Db"]);
    }

    [Fact]
    public void LocalNumberAndBoolean_KeepTheirJsonType()
    {
        var local = Obj("""{ "Port": 80, "Enabled": false, "Ratio": 1.5 }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "Port": "8080", "Enabled": "true", "Ratio": "2.5" }"""));

        Assert.Equal(JsonValueKind.Number, local["Port"]!.GetValueKind());
        Assert.Equal("8080", local["Port"]!.ToJsonString());
        Assert.True((bool)local["Enabled"]!);
        Assert.Equal(2.5m, (decimal)local["Ratio"]!);
    }

    [Fact]
    public void NonParsableValue_ReplacesTypedValueWithString()
    {
        var local = Obj("""{ "Port": 80 }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "Port": "abc" }"""));

        Assert.Equal("abc", (string?)local["Port"]);
    }

    [Fact]
    public void SameValueAfterCoercion_CountsAsUnchanged()
    {
        var local = Obj("""{ "Port": 80, "Name": "x" }""");

        var stats = JsonSettingsMerger.Merge(local, Obj("""{ "Port": "80", "Name": "x" }"""));

        Assert.Equal(new MergeStats(0, 0, 2), stats);
    }

    [Fact]
    public void Arrays_MergeByIndex()
    {
        var local = Obj("""{ "Hosts": ["a", "b", "c"] }""");

        var stats = JsonSettingsMerger.Merge(local, Obj("""{ "Hosts": ["x", "b", "y", "z"] }"""));

        Assert.Equal(["x", "b", "y", "z"], ((JsonArray)local["Hosts"]!).Select(n => (string?)n));
        Assert.Equal(new MergeStats(1, 2, 1), stats);
    }

    [Fact]
    public void TypeMismatch_AzureReplacesLocalNode()
    {
        var local = Obj("""{ "A": { "B": 1 }, "C": "text" }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "A": "flat", "C": { "D": "nested" } }"""));

        Assert.Equal("flat", (string?)local["A"]);
        Assert.Equal("nested", (string?)local["C"]!["D"]);
    }

    [Fact]
    public void NullLocalValue_IsReplaced()
    {
        var local = Obj("""{ "A": null }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "A": "v" }"""));

        Assert.Equal("v", (string?)local["A"]);
    }
}

