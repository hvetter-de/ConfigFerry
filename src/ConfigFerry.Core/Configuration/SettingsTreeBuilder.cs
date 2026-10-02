using System.Globalization;
using System.Text.Json.Nodes;

namespace ConfigFerry.Core.Configuration;

/// <summary>Turns flat "A__B__0" style settings into the nested JSON structure of an appsettings.json.</summary>
public static class SettingsTreeBuilder
{
    public static JsonObject Build(IEnumerable<KeyValuePair<string, string>> settings, ICollection<string> warnings)
    {
        var root = new Node(string.Empty);
        foreach (var (key, value) in settings)
        {
            var segments = ConfigKey.Split(key);
            if (segments.Length == 0)
            {
                continue;
            }

            var current = root;
            foreach (var segment in segments)
            {
                current = current.GetOrAddChild(segment);
            }

            current.Value = value;
        }

        var result = new JsonObject();
        foreach (var child in root.Children.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            result[child.Name] = ToJson(child, child.Name, warnings);
        }

        return result;
    }

    private static JsonNode ToJson(Node node, string path, ICollection<string> warnings)
    {
        if (node.Children.Count == 0)
        {
            return JsonValue.Create(node.Value ?? string.Empty);
        }

        if (node.Value is not null)
        {
            warnings.Add($"'{path}' is used both as a value and as a section; the plain value was ignored.");
        }

        if (TryGetArrayOrder(node, out var ordered))
        {
            var array = new JsonArray();
            foreach (var child in ordered)
            {
                array.Add(ToJson(child, $"{path}:{child.Name}", warnings));
            }

            return array;
        }

        var obj = new JsonObject();
        foreach (var child in node.Children.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            obj[child.Name] = ToJson(child, $"{path}:{child.Name}", warnings);
        }

        return obj;
    }

    /// <summary>A section is an array when its children are exactly the indexes 0..n-1.</summary>
    private static bool TryGetArrayOrder(Node node, out List<Node> ordered)
    {
        ordered = [];
        var indexed = new List<(int Index, Node Node)>();
        foreach (var child in node.Children.Values)
        {
            if (!child.Name.All(char.IsAsciiDigit)
                || !int.TryParse(child.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                return false;
            }

            indexed.Add((index, child));
        }

        indexed.Sort((a, b) => a.Index.CompareTo(b.Index));
        for (var i = 0; i < indexed.Count; i++)
        {
            if (indexed[i].Index != i)
            {
                return false;
            }
        }

        ordered = [.. indexed.Select(x => x.Node)];
        return true;
    }

    private sealed class Node(string name)
    {
        public string Name { get; } = name;

        public string? Value { get; set; }

        public Dictionary<string, Node> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Node GetOrAddChild(string childName)
        {
            if (!Children.TryGetValue(childName, out var child))
            {
                child = new Node(childName);
                Children.Add(childName, child);
            }

            return child;
        }
    }
}
