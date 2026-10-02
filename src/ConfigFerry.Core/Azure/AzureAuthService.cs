using ConfigFerry.Core.Abstractions;
using global::Azure.Core;

namespace ConfigFerry.Core.Azure;

/// <summary>
/// Session logic on top of <see cref="IAuthSessionFactory"/>: remembers who is signed in, keeps one persisted
/// session per tenant (an ARM token is scoped to a single tenant) and re-scopes the credential when the user
/// switches tenants. One credential serves both ARM and Key Vault.
/// </summary>
public sealed class AzureAuthService(IAuthSessionFactory sessions) : IAzureAuthService
{
    /// <summary>Persisted slot of the sign-in tenant; other tenants use their own id as slot.</summary>
    internal const string DefaultSlot = "default";

    private AuthSession? _session;
    private string? _homeTenantId;

    public bool IsSignedIn => _session is not null;

    public string? AccountName => _session?.Username;

    public string? TenantId => _session?.TenantId;

    public async Task<bool> TrySignInSilentlyAsync(CancellationToken cancellationToken)
    {
        var session = await sessions.TryRestoreAsync(DefaultSlot, tenantId: null, cancellationToken);
        if (session is null)
        {
            return false;
        }

        (_session, _homeTenantId) = (session, session.TenantId);
        return true;
    }

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        var session = await sessions.SignInAsync(DefaultSlot, tenantId: null, cancellationToken);
        (_session, _homeTenantId) = (session, session.TenantId);
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

        var scopedTenant = tenantGuid.ToString("D");
        var slot = string.Equals(scopedTenant, _homeTenantId, StringComparison.OrdinalIgnoreCase)
            ? DefaultSlot
            : scopedTenant;

        // Prefer a cached session; only prompt when there is none. The active session is replaced only on success.
        _session = await sessions.TryRestoreAsync(slot, scopedTenant, cancellationToken)
            ?? await sessions.SignInAsync(slot, scopedTenant, cancellationToken);
    }

    public void SignOut()
    {
        (_session, _homeTenantId) = (null, null);
        sessions.ForgetAll();
    }

    public TokenCredential GetCredential() =>
        _session?.Credential ?? throw new ConfigFerryException("You are not signed in to Azure.");
}
