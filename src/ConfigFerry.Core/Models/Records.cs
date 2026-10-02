namespace ConfigFerry.Core.Models;

/// <summary>A Microsoft Entra tenant (directory).</summary>
public sealed record TenantInfo(string Id, string DisplayName);

/// <summary>An Azure subscription the signed-in user can see.</summary>
public sealed record SubscriptionInfo(string Id, string DisplayName);

public enum AppServiceKind
{
    WebApp,
    FunctionApp,
}

/// <summary>A web app or function app inside a subscription.</summary>
public sealed record AppServiceInfo(string ResourceId, string Name, string ResourceGroup, AppServiceKind Kind)
{
    public string DisplayName =>
        $"{Name}  ·  {(Kind == AppServiceKind.FunctionApp ? "Function App" : "Web App")}  ·  {ResourceGroup}";
}

/// <summary>Raw configuration of an app service as stored in Azure.</summary>
public sealed record AzureAppConfiguration(
    IReadOnlyDictionary<string, string> AppSettings,
    IReadOnlyDictionary<string, string> ConnectionStrings);

/// <summary>Layout of the generated file.</summary>
public enum ConfigFormat
{
    /// <summary>Hierarchical appsettings.json for ASP.NET / generic host apps.</summary>
    AppSettings,

    /// <summary>local.settings.json for Azure Functions (flat "Values" section).</summary>
    FunctionLocalSettings,
}

public sealed record GenerationOptions(
    ConfigFormat Format,
    bool ExcludePlatformSettings = true,
    bool IncludeConnectionStrings = true);

public sealed record MergeStats(int Added, int Overridden, int Unchanged)
{
    public static MergeStats Empty { get; } = new(0, 0, 0);

    public static MergeStats operator +(MergeStats a, MergeStats b) =>
        new(a.Added + b.Added, a.Overridden + b.Overridden, a.Unchanged + b.Unchanged);
}

public sealed record GenerationResult(string Json, MergeStats Stats, IReadOnlyList<string> Warnings);

/// <summary>Configuration after Key Vault references were resolved, plus diagnostics.</summary>
public sealed record KeyVaultResolutionResult(
    AzureAppConfiguration Configuration,
    int Resolved,
    IReadOnlyList<string> Warnings);
