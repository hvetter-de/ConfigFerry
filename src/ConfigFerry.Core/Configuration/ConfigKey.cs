namespace ConfigFerry.Core.Configuration;

/// <summary>
/// Azure app settings encode hierarchy as "A:B" or "A__B" (Linux requires the latter). Both mean the same thing.
/// </summary>
public static class ConfigKey
{
    private static readonly string[] Separators = ["__", ":"];

    public static string[] Split(string key) =>
        key.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Canonical flat form using "__", as expected by environment variables and local.settings.json.</summary>
    public static string Normalize(string key) => string.Join("__", Split(key));
}
