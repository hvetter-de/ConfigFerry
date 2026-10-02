using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigFerry.Core.Configuration;
using ConfigFerry.Core.Models;

namespace ConfigFerry.Core.Tests;

[TestClass]
public class JsonSettingsMergerTests
{
    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    [TestMethod]
    public void Azure_Wins_OnScalarConflict_AndLocalOnlyKeysSurvive()
    {
        var local = Obj("""{ "A": "local", "OnlyLocal": "keep" }""");

        var stats = JsonSettingsMerger.Merge(local, Obj("""{ "A": "azure", "OnlyAzure": "new" }"""));

        Assert.AreEqual("azure", (string?)local["A"]);
        Assert.AreEqual("keep", (string?)local["OnlyLocal"]);
        Assert.AreEqual("new", (string?)local["OnlyAzure"]);
        Assert.AreEqual(new MergeStats(1, 1, 0), stats);
    }

    [TestMethod]
    public void Nested_Objects_AreMergedDeeply()
    {
        var local = Obj("""{ "Logging": { "LogLevel": { "Default": "Debug", "System": "Info" } } }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "Logging": { "LogLevel": { "Default": "Warning" } } }"""));

        Assert.AreEqual("Warning", (string?)local["Logging"]!["LogLevel"]!["Default"]);
        Assert.AreEqual("Info", (string?)local["Logging"]!["LogLevel"]!["System"]);
    }

    [TestMethod]
    public void PropertyNames_MatchCaseInsensitively_KeepingLocalCasing()
    {
        var local = Obj("""{ "connectionStrings": { "Db": "old" } }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "ConnectionStrings": { "DB": "new" } }"""));

        Assert.ContainsSingle(local);
        Assert.AreEqual("new", (string?)local["connectionStrings"]!["Db"]);
    }

    [TestMethod]
    public void LocalNumberAndBoolean_KeepTheirJsonType()
    {
        var local = Obj("""{ "Port": 80, "Enabled": false, "Ratio": 1.5 }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "Port": "8080", "Enabled": "true", "Ratio": "2.5" }"""));

        Assert.AreEqual(JsonValueKind.Number, local["Port"]!.GetValueKind());
        Assert.AreEqual("8080", local["Port"]!.ToJsonString());
        Assert.IsTrue((bool)local["Enabled"]!);
        Assert.AreEqual(2.5m, (decimal)local["Ratio"]!);
    }

    [TestMethod]
    public void NonParsableValue_ReplacesTypedValueWithString()
    {
        var local = Obj("""{ "Port": 80 }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "Port": "abc" }"""));

        Assert.AreEqual("abc", (string?)local["Port"]);
    }

    [TestMethod]
    public void SameValueAfterCoercion_CountsAsUnchanged()
    {
        var local = Obj("""{ "Port": 80, "Name": "x" }""");

        var stats = JsonSettingsMerger.Merge(local, Obj("""{ "Port": "80", "Name": "x" }"""));

        Assert.AreEqual(new MergeStats(0, 0, 2), stats);
    }

    [TestMethod]
    public void Arrays_MergeByIndex()
    {
        var local = Obj("""{ "Hosts": ["a", "b", "c"] }""");

        var stats = JsonSettingsMerger.Merge(local, Obj("""{ "Hosts": ["x", "b", "y", "z"] }"""));

        Assert.AreSequenceEqual(["x", "b", "y", "z"], ((JsonArray)local["Hosts"]!).Select(n => (string?)n));
        Assert.AreEqual(new MergeStats(1, 2, 1), stats);
    }

    [TestMethod]
    public void TypeMismatch_AzureReplacesLocalNode()
    {
        var local = Obj("""{ "A": { "B": 1 }, "C": "text" }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "A": "flat", "C": { "D": "nested" } }"""));

        Assert.AreEqual("flat", (string?)local["A"]);
        Assert.AreEqual("nested", (string?)local["C"]!["D"]);
    }

    [TestMethod]
    public void NullLocalValue_IsReplaced()
    {
        var local = Obj("""{ "A": null }""");

        JsonSettingsMerger.Merge(local, Obj("""{ "A": "v" }"""));

        Assert.AreEqual("v", (string?)local["A"]);
    }
}

