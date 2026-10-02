using ConfigFerry.Core.Abstractions;
using global::Azure.Core;
using global::Azure.Identity;

namespace ConfigFerry.Core.Azure;

/// <summary>
/// Microsoft Entra ID sign-in through the system browser. The token cache is persisted (DPAPI) and an
/// authentication record is stored per slot, so a later start can sign in silently. This is the only class that
/// talks to the browser and the file system for authentication; it is deliberately thin.
/// </summary>
public sealed class EntraAuthSessionFactory : IAuthSessionFactory
{
    private static readonly string[] ArmScopes = ["https://management.azure.com/.default"];

    private readonly string _directory;

    public EntraAuthSessionFactory()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConfigFerry"))
    {
    }

    public EntraAuthSessionFactory(string directory) => _directory = directory;

    public async Task<AuthSession?> TryRestoreAsync(string slot, string? tenantId, CancellationToken cancellationToken)
    {
        var path = RecordPath(slot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var record = await AuthenticationRecord.DeserializeAsync(stream, cancellationToken);
            var credential = CreateCredential(tenantId, record, disableAutomaticAuthentication: true);

            // Throws AuthenticationRequiredException when the cached session expired.
            await credential.GetTokenAsync(new TokenRequestContext(ArmScopes, tenantId: tenantId), cancellationToken);
            return new AuthSession(credential, record.Username, record.TenantId);
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

    public async Task<AuthSession> SignInAsync(string slot, string? tenantId, CancellationToken cancellationToken)
    {
        var credential = CreateCredential(tenantId, record: null, disableAutomaticAuthentication: false);
        var record = await credential.AuthenticateAsync(new TokenRequestContext(ArmScopes, tenantId: tenantId), cancellationToken);

        Directory.CreateDirectory(_directory);
        await using (var stream = File.Create(RecordPath(slot)))
        {
            await record.SerializeAsync(stream, cancellationToken);
        }

        return new AuthSession(credential, record.Username, record.TenantId);
    }

    public void ForgetAll()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_directory, "auth-record*.bin"))
        {
            File.Delete(file);
        }
    }

    private string RecordPath(string slot)
    {
        // Slots are either "default" or a tenant GUID; never let anything else reach the file system.
        var name = slot == AzureAuthService.DefaultSlot
            ? "auth-record.bin"
            : $"auth-record-{Guid.Parse(slot):D}.bin";
        return Path.Combine(_directory, name);
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
