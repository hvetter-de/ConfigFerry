using ConfigFerry.Core.Models;

namespace ConfigFerry.Core.Abstractions;

public interface IAzureAuthService
{
    bool IsSignedIn { get; }

    /// <summary>User principal name of the signed-in account, if any.</summary>
    string? AccountName { get; }

    /// <summary>Id of the Entra tenant the current credential is scoped to.</summary>
    string? TenantId { get; }

    /// <summary>
    /// Re-scopes the credential to another tenant (guest/multi-tenant accounts). Uses a cached session when one
    /// exists, otherwise prompts interactively. The previous tenant stays active if this fails.
    /// </summary>
    Task SwitchTenantAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>Restores a previous session without UI. Returns false when interactive sign-in is required.</summary>
    Task<bool> TrySignInSilentlyAsync(CancellationToken cancellationToken);

    /// <summary>Interactive Microsoft Entra ID sign-in (browser).</summary>
    Task SignInAsync(CancellationToken cancellationToken);

    void SignOut();

    /// <summary>The credential to use for ARM and Key Vault calls. Throws when not signed in.</summary>
    global::Azure.Core.TokenCredential GetCredential();
}

public interface IAzureResourceService
{
    /// <summary>Tenants the signed-in account is a member or guest of.</summary>
    Task<IReadOnlyList<TenantInfo>> GetTenantsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AppServiceInfo>> GetAppServicesAsync(string subscriptionId, CancellationToken cancellationToken);

    Task<AzureAppConfiguration> GetConfigurationAsync(AppServiceInfo appService, CancellationToken cancellationToken);
}

public interface ISecretReader
{
    /// <summary>Reads a secret value. <paramref name="version"/> null means latest.</summary>
    Task<string> GetSecretAsync(Uri vaultUri, string secretName, string? version, CancellationToken cancellationToken);
}

public interface IKeyVaultReferenceResolver
{
    Task<KeyVaultResolutionResult> ResolveAsync(AzureAppConfiguration configuration, CancellationToken cancellationToken);
}

public interface IConfigGenerator
{
    /// <param name="existingJson">Content of the local file to merge into; null to generate a fresh document.</param>
    GenerationResult Generate(AzureAppConfiguration azure, GenerationOptions options, string? existingJson);
}

public interface IFilePickerService
{
    Task<string?> PickSettingsFileAsync();
}

public interface IClipboardService
{
    void SetText(string text);
}

public interface ITextFileStore
{
    bool Exists(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);

    void Copy(string source, string destination, bool overwrite);
}

