using ConfigFerry.Core.Abstractions;
using global::Azure.Core;
using global::Azure.Identity;

namespace ConfigFerry.Core.Azure;

/// <summary>
/// Microsoft Entra ID sign-in through the system browser. The token cache is persisted (DPAPI) and an
/// authentication record is remembered per tenant, so the next start can sign in silently and switching back to
/// a tenant needs no new prompt. One credential serves both ARM and Key Vault; MSAL acquires tokens per resource
/// from the same cached session. An ARM token is scoped to a single tenant, hence a credential per tenant.
/// </summary>
public sealed class AzureAuthService : IAzureAuthService
{
    private static readonly string[] ArmScopes = ["https://management.azure.com/.default"];

    private const string DefaultRecordFile = "auth-record.bin";

    private readonly string _directory;
    private InteractiveBrowserCredential? _credential;
    private AuthenticationRecord? _record;
    private string? _homeTenantId;

    public AzureAuthService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConfigFerry"))
    {
    }

    public AzureAuthService(string directory) => _directory = directory;

    public bool IsSignedIn => _credential is not null;

    public string? AccountName => _record?.Username;

    public string? TenantId => _record?.TenantId;

    public async Task<bool> TrySignInSilentlyAsync(CancellationToken cancellationToken)
    {
        var session = await TryRestoreAsync(tenantId: null, Path.Combine(_directory, DefaultRecordFile), cancellationToken);
        if (session is null)
        {
            return false;
        }

        (_credential, _record) = session.Value;
        _homeTenantId = _record.TenantId;
        return true;
    }

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        var session = await AuthenticateAsync(tenantId: null, Path.Combine(_directory, DefaultRecordFile), cancellationToken);
        (_credential, _record) = session;
        _homeTenantId = _record.TenantId;
    }

    public async Task SwitchTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            throw new ArgumentException("The tenant id must be a GUID.", nameof(tenantId));
        }

        if (!IsSignedIn)
        {
            throw new ConfigFerryException("You are not signed in to Azure.");
        }

        if (string.Equals(tenantId, TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isHome = string.Equals(tenantId, _homeTenantId, StringComparison.OrdinalIgnoreCase);
        var recordPath = Path.Combine(
            _directory, isHome ? DefaultRecordFile : $"auth-record-{tenantGuid:D}.bin");
        var scopedTenant = tenantGuid.ToString("D");

        var session = await TryRestoreAsync(scopedTenant, recordPath, cancellationToken)
            ?? await AuthenticateAsync(scopedTenant, recordPath, cancellationToken);

        // Only replace the active session once the new one works.
        (_credential, _record) = session;
    }

    public void SignOut()
    {
        (_credential, _record, _homeTenantId) = (null, null, null);
        if (Directory.Exists(_directory))
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "auth-record*.bin"))
            {
                File.Delete(file);
            }
        }
    }

    public TokenCredential GetCredential() =>
        _credential ?? throw new ConfigFerryException("You are not signed in to Azure.");

    private static async Task<(InteractiveBrowserCredential Credential, AuthenticationRecord Record)?> TryRestoreAsync(
        string? tenantId, string recordPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(recordPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(recordPath);
            var record = await AuthenticationRecord.DeserializeAsync(stream, cancellationToken);
            var credential = CreateCredential(tenantId, record, disableAutomaticAuthentication: true);

            // Throws AuthenticationRequiredException when the cached session expired.
            await credential.GetTokenAsync(new TokenRequestContext(ArmScopes, tenantId: tenantId), cancellationToken);
            return (credential, record);
        }
        catch (AuthenticationRequiredException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or CredentialUnavailableException)
        {
            return null;
        }
    }

    private static async Task<(InteractiveBrowserCredential Credential, AuthenticationRecord Record)> AuthenticateAsync(
        string? tenantId, string recordPath, CancellationToken cancellationToken)
    {
        var credential = CreateCredential(tenantId, record: null, disableAutomaticAuthentication: false);
        var record = await credential.AuthenticateAsync(new TokenRequestContext(ArmScopes, tenantId: tenantId), cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
        await using var stream = File.Create(recordPath);
        await record.SerializeAsync(stream, cancellationToken);
        return (credential, record);
    }

    private static InteractiveBrowserCredential CreateCredential(
        string? tenantId, AuthenticationRecord? record, bool disableAutomaticAuthentication) =>
        new(new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId,
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "ConfigFerry" },
            AuthenticationRecord = record,
            DisableAutomaticAuthentication = disableAutomaticAuthentication,
        });
}
