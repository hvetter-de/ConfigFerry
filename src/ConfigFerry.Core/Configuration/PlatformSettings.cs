namespace ConfigFerry.Core.Configuration;

/// <summary>Settings owned by the Azure hosting platform that are meaningless (or harmful) on a dev machine.</summary>
public static class PlatformSettings
{
    private static readonly string[] Prefixes =
    [
        "WEBSITE_", "WEBSITES_", "SCM_", "XDT_", "DIAGNOSTICSERVICES_", "SNAPSHOTDEBUGGER_",
        "INSTRUMENTATIONENGINE_", "APPINSIGHTS_PROFILERFEATURE", "APPINSIGHTS_SNAPSHOTFEATURE",
    ];

    public static bool IsPlatformSetting(string key) =>
        string.Equals(key, "FUNCTIONS_EXTENSION_VERSION", StringComparison.OrdinalIgnoreCase)
        || Prefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
