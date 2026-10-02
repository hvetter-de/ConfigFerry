using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Models;

namespace ConfigFerry.Core.Configuration;

public sealed class ConfigGenerator : IConfigGenerator
{
    private const string ConnectionStringsSection = "ConnectionStrings";

    // Relaxed escaping keeps '&', '+', '<' etc. readable; connection strings are full of them.
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public GenerationResult Generate(AzureAppConfiguration azure, GenerationOptions options, string? existingJson)
    {
        ArgumentNullException.ThrowIfNull(azure);
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();
        var existing = string.IsNullOrWhiteSpace(existingJson) ? null : ParseExisting(existingJson, warnings);

        var settings = azure.AppSettings
            .Where(s => !options.ExcludePlatformSettings || !PlatformSettings.IsPlatformSetting(s.Key))
            .ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);
        var connectionStrings = options.IncludeConnectionStrings
            ? azure.ConnectionStrings.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase)
            : [];

        var (root, stats) = options.Format switch
        {
            ConfigFormat.AppSettings => BuildAppSettings(existing, settings, connectionStrings, warnings),
            ConfigFormat.FunctionLocalSettings => BuildFunctionSettings(existing, settings, connectionStrings),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Format, "Unknown format."),
        };

        return new GenerationResult(root.ToJsonString(OutputOptions), stats, warnings);
    }

    private static (JsonObject Root, MergeStats Stats) BuildAppSettings(
        JsonObject? existing,
        Dictionary<string, string> settings,
        Dictionary<string, string> connectionStrings,
        List<string> warnings)
    {
        var flat = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in connectionStrings)
        {
            flat[$"{ConnectionStringsSection}__{name}"] = value;
        }

        var azureTree = SettingsTreeBuilder.Build(flat, warnings);
        if (existing is null)
        {
            return (azureTree, new MergeStats(JsonSettingsMerger.CountLeaves(azureTree), 0, 0));
        }

        var stats = JsonSettingsMerger.Merge(existing, azureTree);
        return (existing, stats);
    }

    private static (JsonObject Root, MergeStats Stats) BuildFunctionSettings(
        JsonObject? existing,
        Dictionary<string, string> settings,
        Dictionary<string, string> connectionStrings)
    {
        var root = existing ?? [];
        if (FindKey(root, "IsEncrypted") is null)
        {
            root.Insert(0, "IsEncrypted", false);
        }

        var values = GetOrAddSection(root, "Values");
        var stats = MergeFlat(values, settings, normalizeKeys: true);

        if (connectionStrings.Count > 0)
        {
            var section = GetOrAddSection(root, ConnectionStringsSection);
            stats += MergeFlat(section, connectionStrings, normalizeKeys: false);
        }

        return (root, stats);
    }

    /// <summary>Merges flat string pairs into a flat JSON object; Azure wins, matching is case-insensitive.</summary>
    private static MergeStats MergeFlat(JsonObject target, Dictionary<string, string> source, bool normalizeKeys)
    {
        string Canonical(string key) => normalizeKeys ? ConfigKey.Normalize(key) : key;

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, _) in target)
        {
            index.TryAdd(Canonical(key), key);
        }

        int added = 0, overridden = 0, unchanged = 0;
        foreach (var (rawKey, value) in source.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = Canonical(rawKey);
            if (!index.TryGetValue(key, out var existingKey))
            {
                target[key] = value;
                index[key] = key;
                added++;
                continue;
            }

            var current = target[existingKey];
            var currentText = current is JsonValue v && v.TryGetValue<string>(out var s) ? s : current?.ToJsonString();
            if (currentText == value)
            {
                unchanged++;
                continue;
            }

            if (!string.Equals(existingKey, key, StringComparison.Ordinal))
            {
                target.Remove(existingKey);
                index[key] = key;
            }

            target[key] = value;
            overridden++;
        }

        return new MergeStats(added, overridden, unchanged);
    }

    private static JsonObject GetOrAddSection(JsonObject root, string name)
    {
        var existingName = FindKey(root, name);
        if (existingName is null)
        {
            var created = new JsonObject();
            root[name] = created;
            return created;
        }

        return root[existingName] as JsonObject
            ?? throw new ConfigFerryException($"The '{existingName}' section of the existing file must be a JSON object.");
    }

    private static string? FindKey(JsonObject obj, string name) =>
        obj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    private static JsonObject ParseExisting(string json, List<string> warnings)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
        }
        catch (JsonException)
        {
            try
            {
                node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                warnings.Add("The existing file contains comments or trailing commas. They are not preserved when the file is rewritten.");
            }
            catch (JsonException ex)
            {
                throw new ConfigFerryException($"The existing file is not valid JSON: {ex.Message}", ex);
            }
        }

        return node as JsonObject
            ?? throw new ConfigFerryException("The existing file must contain a JSON object at the top level.");
    }
}
