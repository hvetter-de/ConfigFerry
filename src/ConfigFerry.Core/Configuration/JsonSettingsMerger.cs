using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigFerry.Core.Models;

namespace ConfigFerry.Core.Configuration;

/// <summary>
/// Deep-merges the Azure settings tree into a local appsettings document. Azure always wins.
/// Property names match case-insensitively (like .NET configuration) and the local casing is kept.
/// Arrays merge by index, which mirrors how the .NET configuration system layers sources.
/// </summary>
public static class JsonSettingsMerger
{
    public static MergeStats Merge(JsonObject target, JsonObject source)
    {
        var counter = new Counter();
        MergeObject(target, source, counter);
        return new MergeStats(counter.Added, counter.Overridden, counter.Unchanged);
    }

    public static int CountLeaves(JsonNode? node) => node switch
    {
        JsonObject o => o.Sum(p => CountLeaves(p.Value)),
        JsonArray a => a.Sum(CountLeaves),
        _ => 1,
    };

    private static void MergeObject(JsonObject target, JsonObject source, Counter counter)
    {
        foreach (var (name, sourceValue) in source)
        {
            if (sourceValue is null)
            {
                continue;
            }

            var existingName = target.Select(p => p.Key)
                .FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            if (existingName is null)
            {
                target[name] = sourceValue.DeepClone();
                counter.Added += CountLeaves(sourceValue);
                continue;
            }

            var existing = target[existingName];
            var merged = MergeNode(existing, sourceValue, counter);
            if (!ReferenceEquals(merged, existing))
            {
                target[existingName] = merged;
            }
        }
    }

    private static JsonNode? MergeNode(JsonNode? existing, JsonNode source, Counter counter)
    {
        switch (existing, source)
        {
            case (JsonObject existingObject, JsonObject sourceObject):
                MergeObject(existingObject, sourceObject, counter);
                return existingObject;

            case (JsonArray existingArray, JsonArray sourceArray):
                for (var i = 0; i < sourceArray.Count; i++)
                {
                    var item = sourceArray[i];
                    if (item is null)
                    {
                        continue;
                    }

                    if (i >= existingArray.Count)
                    {
                        existingArray.Add(item.DeepClone());
                        counter.Added += CountLeaves(item);
                        continue;
                    }

                    var current = existingArray[i];
                    var merged = MergeNode(current, item, counter);
                    if (!ReferenceEquals(merged, current))
                    {
                        existingArray[i] = merged;
                    }
                }

                return existingArray;

            case (JsonValue existingValue, JsonValue sourceValue):
                var coerced = CoerceToExistingType(existingValue, sourceValue);
                if (JsonNode.DeepEquals(existingValue, coerced))
                {
                    counter.Unchanged++;
                    return existingValue;
                }

                counter.Overridden++;
                return coerced;

            default:
                counter.Overridden += CountLeaves(source);
                return source.DeepClone();
        }
    }

    /// <summary>
    /// Azure only stores strings. When the local file holds a number or boolean and the Azure string
    /// represents the same kind of value, keep the local JSON type so the file stays idiomatic.
    /// </summary>
    private static JsonNode CoerceToExistingType(JsonValue existing, JsonValue azure)
    {
        if (azure.TryGetValue<string>(out var text))
        {
            switch (existing.GetValueKind())
            {
                case JsonValueKind.True or JsonValueKind.False when bool.TryParse(text, out var boolean):
                    return JsonValue.Create(boolean);
                case JsonValueKind.Number when decimal.TryParse(
                    text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
                    return JsonValue.Create(number);
            }
        }

        return azure.DeepClone();
    }

    private sealed class Counter
    {
        public int Added { get; set; }

        public int Overridden { get; set; }

        public int Unchanged { get; set; }
    }
}
