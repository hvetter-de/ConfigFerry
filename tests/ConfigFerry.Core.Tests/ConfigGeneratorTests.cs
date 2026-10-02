using System.Text.Json.Nodes;
using ConfigFerry.Core.Configuration;
using ConfigFerry.Core.Models;

namespace ConfigFerry.Core.Tests;

[TestClass]
public class ConfigGeneratorTests
{
    private readonly ConfigGenerator _sut = new();

    private static AzureAppConfiguration Azure(
        Dictionary<string, string>? settings = null, Dictionary<string, string>? connections = null) =>
        new(settings ?? [], connections ?? []);

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    // ---- appsettings.json ----

    [TestMethod]
    public void AppSettings_Fresh_IsNestedAndIncludesConnectionStrings()
    {
        var result = _sut.Generate(
            Azure(
                new() { ["Logging:LogLevel:Default"] = "Warning", ["Feature__Enabled"] = "true" },
                new() { ["Db"] = "Server=x;Password=a&b" }),
            new GenerationOptions(ConfigFormat.AppSettings),
            existingJson: null);

        var json = Parse(result.Json);
        Assert.AreEqual("Warning", (string?)json["Logging"]!["LogLevel"]!["Default"]);
        Assert.AreEqual("true", (string?)json["Feature"]!["Enabled"]);
        Assert.AreEqual("Server=x;Password=a&b", (string?)json["ConnectionStrings"]!["Db"]);
        Assert.Contains("a&b", result.Json); // not escaped as &
        Assert.AreEqual(new MergeStats(3, 0, 0), result.Stats);
    }

    [TestMethod]
    public void AppSettings_ExcludesPlatformSettings_ByDefault_AndCanKeepThem()
    {
        var azure = Azure(new() { ["WEBSITE_RUN_FROM_PACKAGE"] = "1", ["My"] = "v" });

        var excluded = _sut.Generate(azure, new GenerationOptions(ConfigFormat.AppSettings), null);
        var included = _sut.Generate(azure, new GenerationOptions(ConfigFormat.AppSettings, ExcludePlatformSettings: false), null);

        Assert.DoesNotContain("WEBSITE_RUN_FROM_PACKAGE", excluded.Json);
        Assert.Contains("WEBSITE_RUN_FROM_PACKAGE", included.Json);
    }

    [TestMethod]
    public void AppSettings_CanSkipConnectionStrings()
    {
        var result = _sut.Generate(
            Azure(connections: new() { ["Db"] = "x" }),
            new GenerationOptions(ConfigFormat.AppSettings, IncludeConnectionStrings: false),
            null);

        Assert.DoesNotContain("ConnectionStrings", result.Json);
    }

    [TestMethod]
    public void AppSettings_MergeIntoExisting_AzureWins_LocalKept_CommentsWarned()
    {
        const string existing = """
            {
              // local only
              "Logging": { "LogLevel": { "Default": "Debug" } },
              "LocalOnly": "keep",
              "Port": 80,
            }
            """;

        var result = _sut.Generate(
            Azure(new() { ["Logging__LogLevel__Default"] = "Warning", ["Port"] = "8080", ["New"] = "n" }),
            new GenerationOptions(ConfigFormat.AppSettings),
            existing);

        var json = Parse(result.Json);
        Assert.AreEqual("Warning", (string?)json["Logging"]!["LogLevel"]!["Default"]);
        Assert.AreEqual("keep", (string?)json["LocalOnly"]);
        Assert.AreEqual(8080, (int)json["Port"]!);
        Assert.AreEqual("n", (string?)json["New"]);
        Assert.Contains(w => w.Contains("comments", StringComparison.OrdinalIgnoreCase), result.Warnings);
        Assert.AreEqual(new MergeStats(1, 2, 0), result.Stats);
    }

    [TestMethod]
    [DataRow("{ not json")]
    [DataRow("[1,2]")]
    [DataRow("\"text\"")]
    public void InvalidExistingFile_Throws(string existing) =>
        Assert.ThrowsExactly<ConfigFerryException>(() =>
            _sut.Generate(Azure(), new GenerationOptions(ConfigFormat.AppSettings), existing));

    [TestMethod]
    public void BlankExistingFile_IsTreatedAsNew()
    {
        var result = _sut.Generate(Azure(new() { ["A"] = "1" }), new GenerationOptions(ConfigFormat.AppSettings), "  \n");

        Assert.AreEqual("1", (string?)Parse(result.Json)["A"]);
    }

    // ---- local.settings.json ----

    [TestMethod]
    public void Functions_Fresh_HasFlatValuesWithDoubleUnderscore()
    {
        var result = _sut.Generate(
            Azure(
                new() { ["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated", ["App:Setting"] = "v", ["WEBSITE_X"] = "1" },
                new() { ["Db"] = "conn" }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings),
            null);

        var json = Parse(result.Json);
        Assert.IsFalse((bool)json["IsEncrypted"]!);
        var values = (JsonObject)json["Values"]!;
        Assert.AreEqual("dotnet-isolated", (string?)values["FUNCTIONS_WORKER_RUNTIME"]);
        Assert.AreEqual("v", (string?)values["App__Setting"]);
        Assert.IsFalse(values.ContainsKey("WEBSITE_X"));
        Assert.AreEqual("conn", (string?)values["ConnectionStrings__Db"]);
        Assert.IsFalse(json.ContainsKey("ConnectionStrings")); // no separate section by default
        foreach (var (_, value) in values)
        {
            Assert.IsInstanceOfType<JsonValue>(value);
        }
    }

    [TestMethod]
    public void Functions_ConnectionStrings_GoToValues_WhenFileHasNoSection()
    {
        var result = _sut.Generate(
            Azure(connections: new() { ["Orders"] = "c1" }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings),
            """{ "IsEncrypted": false, "Values": { "A": "1" }, "Host": { "CORS": "*" } }""");

        var json = Parse(result.Json);
        Assert.AreEqual("c1", (string?)json["Values"]!["ConnectionStrings__Orders"]);
        Assert.IsFalse(json.ContainsKey("ConnectionStrings"));
        Assert.AreEqual(new MergeStats(1, 0, 0), result.Stats);
    }

    [TestMethod]
    public void Functions_ConnectionString_UpdatedInPlace_WhenExistingSectionManagesIt()
    {
        const string existing = """
            {
              "IsEncrypted": false,
              "Values": { "connectionstrings:orders": "stale", "Other": "x" },
              "ConnectionStrings": { "orders": "old", "LocalOnly": "keep" }
            }
            """;

        var result = _sut.Generate(
            Azure(connections: new() { ["Orders"] = "new", ["Fresh"] = "f" }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings),
            existing);

        var json = Parse(result.Json);
        var section = (JsonObject)json["ConnectionStrings"]!;
        Assert.AreEqual("new", (string?)section["orders"]); // local casing kept
        Assert.AreEqual("keep", (string?)section["LocalOnly"]);
        Assert.AreEqual(2, section.Count);

        var values = (JsonObject)json["Values"]!;
        Assert.IsFalse(values.ContainsKey("connectionstrings:orders")); // conflicting duplicate removed
        Assert.AreEqual("f", (string?)values["ConnectionStrings__Fresh"]); // unknown name goes to Values
        Assert.AreEqual("x", (string?)values["Other"]);
        Assert.Contains(w => w.Contains("Orders", StringComparison.Ordinal) && w.Contains("duplicate", StringComparison.OrdinalIgnoreCase), result.Warnings);
    }

    [TestMethod]
    public void Functions_ConnectionString_WinsOverAppSettingWithSameKey()
    {
        var result = _sut.Generate(
            Azure(
                new() { ["ConnectionStrings:Db"] = "from-setting", ["Other"] = "o" },
                new() { ["Db"] = "from-connection-string" }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings),
            existingJson: null);

        var values = (JsonObject)Parse(result.Json)["Values"]!;
        Assert.AreEqual("from-connection-string", (string?)values["ConnectionStrings__Db"]);
        Assert.AreEqual(2, values.Count(p => p.Key is "ConnectionStrings__Db" or "Other"));
        Assert.AreEqual(2, values.Count);
    }

    [TestMethod]
    public void Functions_ConnectionString_ReplacesExistingValuesEntry_Case_And_Separator_Insensitive()
    {
        var result = _sut.Generate(
            Azure(connections: new() { ["Db"] = "new" }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings),
            """{ "IsEncrypted": false, "Values": { "ConnectionStrings:DB": "old" } }""");

        var values = (JsonObject)Parse(result.Json)["Values"]!;
        Assert.ContainsSingle(values);
        Assert.AreEqual("new", (string?)values["ConnectionStrings__Db"]);
        Assert.AreEqual(new MergeStats(0, 1, 0), result.Stats);
    }

    [TestMethod]
    public void Functions_ConnectionsSectionNotAnObject_Throws() =>
        Assert.ThrowsExactly<ConfigFerryException>(() => _sut.Generate(
            Azure(connections: new() { ["Db"] = "x" }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings),
            """{ "ConnectionStrings": 5 }"""));

    [TestMethod]
    public void Functions_CanSkipConnectionStrings()
    {
        var result = _sut.Generate(
            Azure(new() { ["A"] = "1" }, new() { ["Db"] = "x" }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings, IncludeConnectionStrings: false),
            null);

        Assert.DoesNotContain("ConnectionStrings", result.Json);
    }

    [TestMethod]
    public void Functions_Merge_PreservesHostCorsAndLocalValues_AzureWins()
    {
        const string existing = """
            {
              "IsEncrypted": false,
              "Values": { "AzureWebJobsStorage": "UseDevelopmentStorage=true", "Local": "keep", "app:setting": "old" },
              "Host": { "CORS": "*" }
            }
            """;

        var result = _sut.Generate(
            Azure(new()
            {
                ["AzureWebJobsStorage"] = "prod",
                ["App__Setting"] = "new",
                ["Fresh"] = "f",
            }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings),
            existing);

        var json = Parse(result.Json);
        var values = (JsonObject)json["Values"]!;
        Assert.AreEqual("prod", (string?)values["AzureWebJobsStorage"]);
        Assert.AreEqual("keep", (string?)values["Local"]);
        Assert.AreEqual("new", (string?)values["App__Setting"]);
        Assert.IsFalse(values.ContainsKey("app:setting")); // replaced, not duplicated
        Assert.AreEqual("f", (string?)values["Fresh"]);
        Assert.AreEqual("*", (string?)json["Host"]!["CORS"]);
        Assert.AreEqual(new MergeStats(1, 2, 0), result.Stats);
    }

    [TestMethod]
    public void Functions_Merge_AddsMissingIsEncryptedAndValues()
    {
        var result = _sut.Generate(
            Azure(new() { ["A"] = "1" }), new GenerationOptions(ConfigFormat.FunctionLocalSettings), "{ \"Host\": {} }");

        var json = Parse(result.Json);
        Assert.IsFalse((bool)json["IsEncrypted"]!);
        Assert.AreEqual("1", (string?)json["Values"]!["A"]);
        Assert.IsNotNull(json["Host"]);
    }

    [TestMethod]
    public void Functions_ValuesNotAnObject_Throws() =>
        Assert.ThrowsExactly<ConfigFerryException>(() =>
            _sut.Generate(Azure(), new GenerationOptions(ConfigFormat.FunctionLocalSettings), "{ \"Values\": 5 }"));

    [TestMethod]
    public void Functions_SameValue_CountsUnchanged()
    {
        var result = _sut.Generate(
            Azure(new() { ["A"] = "1" }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings),
            """{ "IsEncrypted": false, "Values": { "A": "1" } }""");

        Assert.AreEqual(new MergeStats(0, 0, 1), result.Stats);
    }
}
