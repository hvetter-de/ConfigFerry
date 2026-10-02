using ConfigFerry.Core.Abstractions;
using global::Azure.Security.KeyVault.Secrets;

namespace ConfigFerry.Core.Azure;

public sealed class SecretClientFactory(IAzureAuthService auth) : ISecretClientFactory
{
    public SecretClient Create(Uri vaultUri) => new(vaultUri, auth.GetCredential());
}
