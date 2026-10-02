using ConfigFerry.Core.Abstractions;
using global::Azure.ResourceManager;

namespace ConfigFerry.Core.Azure;

public sealed class ArmClientFactory(IAzureAuthService auth) : IArmClientFactory
{
    // The credential changes on sign-in / tenant switch, so a client is created per use.
    public ArmClient Create() => new(auth.GetCredential());
}
