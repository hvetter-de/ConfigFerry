using ConfigFerry.Core.Models;

namespace ConfigFerry.Core.Azure;

/// <summary>Pure translation of Azure Resource Manager data into ConfigFerry models (no SDK calls, easy to test).</summary>
internal static class ArmMapping
{
    /// <summary>Function apps report a kind such as "functionapp,linux"; everything else is treated as a web app.</summary>
    public static AppServiceKind GetKind(string? kind) =>
        kind?.Contains("functionapp", StringComparison.OrdinalIgnoreCase) == true
            ? AppServiceKind.FunctionApp
            : AppServiceKind.WebApp;

    /// <summary>"Name (domain)" when both are known; falls back to the domain, then the id. Null without an id.</summary>
    public static TenantInfo? ToTenant(Guid? id, string? displayName, string? defaultDomain)
    {
        if (id is not { } tenantId)
        {
            return null;
        }

        var name = !string.IsNullOrWhiteSpace(displayName) ? displayName
            : !string.IsNullOrWhiteSpace(defaultDomain) ? defaultDomain
            : tenantId.ToString();
        var label = !string.IsNullOrWhiteSpace(defaultDomain) && defaultDomain != name ? $"{name} ({defaultDomain})" : name;
        return new TenantInfo(tenantId.ToString(), label);
    }

    /// <summary>Case-insensitive name to value map; entries without a value are dropped.</summary>
    public static Dictionary<string, string> ToValueMap(IEnumerable<KeyValuePair<string, string?>> source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            if (value is not null)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
