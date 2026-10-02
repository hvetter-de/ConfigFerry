using ConfigFerry.Core.Abstractions;
using global::Azure.Security.KeyVault.Secrets;

namespace ConfigFerry.Core.Azure;

public sealed class KeyVaultSecretReader(IAzureAuthService auth) : ISecretReader
{
    public async Task<string> GetSecretAsync(
        Uri vaultUri, string secretName, string? version, CancellationToken cancellationToken)
    {
        var client = new SecretClient(vaultUri, auth.GetCredential());
        var response = await client.GetSecretAsync(secretName, version, cancellationToken);
        return response.Value.Value;
    }
}
