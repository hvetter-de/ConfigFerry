using ConfigFerry.Core.Abstractions;

namespace ConfigFerry.Core.Azure;

public sealed class KeyVaultSecretReader(ISecretClientFactory clients) : ISecretReader
{
    public async Task<string> GetSecretAsync(
        Uri vaultUri, string secretName, string? version, CancellationToken cancellationToken)
    {
        var response = await clients.Create(vaultUri).GetSecretAsync(secretName, version, cancellationToken);
        return response.Value.Value;
    }
}
