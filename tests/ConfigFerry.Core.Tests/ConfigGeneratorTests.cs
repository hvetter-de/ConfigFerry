using System.Text.Json.Nodes;
using ConfigFerry.Core.Configuration;
using ConfigFerry.Core.Models;

namespace ConfigFerry.Core.Tests;

public class ConfigGeneratorTests
{
    private readonly ConfigGenerator _sut = new();

    private static AzureAppConfiguration Azure(
        Dictionary<string, string>? settings = null, Dictionary<string, string>? connections = null) =>
        new(settings ?? [], connections ?? []);

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    // ---- appsettings.json ----

    [Fact]
    public void AppSettings_Fresh_IsNestedAndIncludesConnectionStrings()
    {
        var result = _sut.Generate(
            Azure(
                new() { ["Logging:LogLevel:Default"] = "Warning", ["Feature__Enabled"] = "true" },
                new() { ["Db"] = "Server=x;Password=a&b" }),
            new GenerationOptions(ConfigFormat.AppSettings),
            existingJson: null);

        var json = Parse(result.Json);
        Assert.Equal("Warning", (string?)json["Logging"]!["LogLevel"]!["Default"]);
        Assert.Equal("true", (string?)json["Feature"]!["Enabled"]);
        Assert.Equal("Server=x;Password=a&b", (string?)json["ConnectionStrings"]!["Db"]);
        Assert.Contains("a&b", result.Json); // not escaped as &
        Assert.Equal(new MergeStats(3, 0, 0), result.Stats);
    }

    [Fact]
    public void AppSettings_ExcludesPlatformSettings_ByDefault_AndCanKeepThem()
    {
        var azure = Azure(new() { ["WEBSITE_RUN_FROM_PACKAGE"] = "1", ["My"] = "v" });

        var excluded = _sut.Generate(azure, new GenerationOptions(ConfigFormat.AppSettings), null);
        var included = _sut.Generate(azure, new GenerationOptions(ConfigFormat.AppSettings, ExcludePlatformSettings: false), null);

        Assert.DoesNotContain("WEBSITE_RUN_FROM_PACKAGE", excluded.Json);
        Assert.Contains("WEBSITE_RUN_FROM_PACKAGE", included.Json);
    }

    [Fact]
    public void AppSettings_CanSkipConnectionStrings()
    {
        var result = _sut.Generate(
            Azure(connections: new() { ["Db"] = "x" }),
            new GenerationOptions(ConfigFormat.AppSettings, IncludeConnectionStrings: false),
            null);

        Assert.DoesNotContain("ConnectionStrings", result.Json);
    }

    [Fact]
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
        Assert.Equal("Warning", (string?)json["Logging"]!["LogLevel"]!["Default"]);
        Assert.Equal("keep", (string?)json["LocalOnly"]);
        Assert.Equal(8080, (int)json["Port"]!);
        Assert.Equal("n", (string?)json["New"]);
        Assert.Contains(result.Warnings, w => w.Contains("comments", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new MergeStats(1, 2, 0), result.Stats);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    public void InvalidExistingFile_Throws(string existing) =>
        Assert.Throws<ConfigFerryException>(() =>
            _sut.Generate(Azure(), new GenerationOptions(ConfigFormat.AppSettings), existing));

    [Fact]
    public void BlankExistingFile_IsTreatedAsNew()
    {
        var result = _sut.Generate(Azure(new() { ["A"] = "1" }), new GenerationOptions(ConfigFormat.AppSettings), "  \n");

        Assert.Equal("1", (string?)Parse(result.Json)["A"]);
    }

    // ---- local.settings.json ----

    [Fact]
    public void Functions_Fresh_HasFlatValuesWithDoubleUnderscore()
    {
        var result = _sut.Generate(
            Azure(
                new() { ["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated", ["App:Setting"] = "v", ["WEBSITE_X"] = "1" },
                new() { ["Db"] = "conn" }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings),
            null);

        var json = Parse(result.Json);
        Assert.False((bool)json["IsEncrypted"]!);
        var values = (JsonObject)json["Values"]!;
        Assert.Equal("dotnet-isolated", (string?)values["FUNCTIONS_WORKER_RUNTIME"]);
        Assert.Equal("v", (string?)values["App__Setting"]);
        Assert.False(values.ContainsKey("WEBSITE_X"));
        Assert.Equal("conn", (string?)json["ConnectionStrings"]!["Db"]);
        Assert.All(values, p => Assert.IsAssignableFrom<JsonValue>(p.Value));
    }

    [Fact]
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
        Assert.Equal("prod", (string?)values["AzureWebJobsStorage"]);
        Assert.Equal("keep", (string?)values["Local"]);
        Assert.Equal("new", (string?)values["App__Setting"]);
        Assert.False(values.ContainsKey("app:setting")); // replaced, not duplicated
        Assert.Equal("f", (string?)values["Fresh"]);
        Assert.Equal("*", (string?)json["Host"]!["CORS"]);
        Assert.Equal(new MergeStats(1, 2, 0), result.Stats);
    }

    [Fact]
    public void Functions_Merge_AddsMissingIsEncryptedAndValues()
    {
        var result = _sut.Generate(
            Azure(new() { ["A"] = "1" }), new GenerationOptions(ConfigFormat.FunctionLocalSettings), "{ \"Host\": {} }");

        var json = Parse(result.Json);
        Assert.False((bool)json["IsEncrypted"]!);
        Assert.Equal("1", (string?)json["Values"]!["A"]);
        Assert.NotNull(json["Host"]);
    }

    [Fact]
    public void Functions_ValuesNotAnObject_Throws() =>
        Assert.Throws<ConfigFerryException>(() =>
            _sut.Generate(Azure(), new GenerationOptions(ConfigFormat.FunctionLocalSettings), "{ \"Values\": 5 }"));

    [Fact]
    public void Functions_SameValue_CountsUnchanged()
    {
        var result = _sut.Generate(
            Azure(new() { ["A"] = "1" }),
            new GenerationOptions(ConfigFormat.FunctionLocalSettings),
            """{ "IsEncrypted": false, "Values": { "A": "1" } }""");

        Assert.Equal(new MergeStats(0, 0, 1), result.Stats);
    }
}
