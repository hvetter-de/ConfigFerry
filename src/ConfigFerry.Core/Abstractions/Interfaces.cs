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

/// <summary>An authenticated Entra session: the credential plus who it belongs to.</summary>
public sealed record AuthSession(global::Azure.Core.TokenCredential Credential, string Username, string TenantId);

/// <summary>
/// Creates and restores Entra sessions (browser sign-in, persisted token cache). Isolates everything that needs a
/// browser or the file system so <see cref="IAzureAuthService"/> stays pure logic.
/// </summary>
public interface IAuthSessionFactory
{
    /// <summary>Restores a persisted session silently; null when an interactive sign-in is required.</summary>
    /// <param name="slot">Name of the persisted session (one per tenant).</param>
    /// <param name="tenantId">Tenant to scope the credential to; null for the account's home tenant.</param>
    Task<AuthSession?> TryRestoreAsync(string slot, string? tenantId, CancellationToken cancellationToken);

    /// <summary>Interactive sign-in; persists the session under <paramref name="slot"/>.</summary>
    Task<AuthSession> SignInAsync(string slot, string? tenantId, CancellationToken cancellationToken);

    /// <summary>Forgets every persisted session.</summary>
    void ForgetAll();
}

/// <summary>Creates the ARM client for the currently signed-in user (a seam for substituting the SDK in tests).</summary>
public interface IArmClientFactory
{
    global::Azure.ResourceManager.ArmClient Create();
}

/// <summary>Creates Key Vault secret clients for the currently signed-in user.</summary>
public interface ISecretClientFactory
{
    global::Azure.Security.KeyVault.Secrets.SecretClient Create(Uri vaultUri);
}

/// <summary>Gives platform services the handle of the main window (pickers need it in unpackaged apps).</summary>
public interface IWindowHandleProvider
{
    nint Handle { get; }
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

