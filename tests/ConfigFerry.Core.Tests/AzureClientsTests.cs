using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Models;
using Azure.Security.KeyVault.Secrets;
using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Azure;
using NSubstitute;

namespace ConfigFerry.Core.Tests;

[TestClass]
public class KeyVaultSecretReaderTests
{
    private static readonly Uri Vault = new("https://shop-kv.vault.azure.net/");

    private readonly ISecretClientFactory _clients = Substitute.For<ISecretClientFactory>();
    private readonly SecretClient _client = Substitute.For<SecretClient>();

    [TestInitialize]
    public void Setup() => _clients.Create(Vault).Returns(_client);

    private static Response<KeyVaultSecret> Secret(string name, string value) =>
        Response.FromValue(SecretModelFactory.KeyVaultSecret(new SecretProperties(name), value), Substitute.For<Response>());

    [TestMethod]
    public async Task ReadsSecretValue_FromTheRequestedVault_AndVersion()
    {
        _client.GetSecretAsync("db-pass", "v1", Arg.Any<CancellationToken>()).Returns(Secret("db-pass", "s3cret"));
        var sut = new KeyVaultSecretReader(_clients);

        var value = await sut.GetSecretAsync(Vault, "db-pass", "v1", CancellationToken.None);

        Assert.AreEqual("s3cret", value);
    }

    [TestMethod]
    public async Task WithoutVersion_AsksForLatest()
    {
        _client.GetSecretAsync("db-pass", null, Arg.Any<CancellationToken>()).Returns(Secret("db-pass", "latest"));
        var sut = new KeyVaultSecretReader(_clients);

        Assert.AreEqual("latest", await sut.GetSecretAsync(Vault, "db-pass", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task ServiceErrors_Propagate_ForTheResolverToReport()
    {
        _client.GetSecretAsync("x", null, Arg.Any<CancellationToken>()).Returns<Response<KeyVaultSecret>>(_ => throw new RequestFailedException(403, "forbidden"));
        var sut = new KeyVaultSecretReader(_clients);

        var ex = await Assert.ThrowsExactlyAsync<RequestFailedException>(() => sut.GetSecretAsync(Vault, "x", null, CancellationToken.None));

        Assert.AreEqual(403, ex.Status);
    }
}

[TestClass]
public class ClientFactoryTests
{
    private readonly IAzureAuthService _auth = Substitute.For<IAzureAuthService>();

    [TestMethod]
    public void SecretClientFactory_CreatesClientForTheVault_WithCurrentCredential()
    {
        _auth.GetCredential().Returns(Substitute.For<TokenCredential>());
        var vault = new Uri("https://shop-kv.vault.azure.net/");

        var client = new SecretClientFactory(_auth).Create(vault);

        Assert.AreEqual(vault, client.VaultUri);
        _auth.Received(1).GetCredential();
    }

    [TestMethod]
    public void ArmClientFactory_CreatesClientWithCurrentCredential()
    {
        _auth.GetCredential().Returns(Substitute.For<TokenCredential>());

        Assert.IsNotNull(new ArmClientFactory(_auth).Create());
        _auth.Received(1).GetCredential();
    }

    [TestMethod]
    public void Factories_WhenSignedOut_ThrowTheFriendlyError()
    {
        _auth.GetCredential().Returns(_ => throw new ConfigFerryException("You are not signed in to Azure."));

        Assert.ThrowsExactly<ConfigFerryException>(() => new ArmClientFactory(_auth).Create());
        Assert.ThrowsExactly<ConfigFerryException>(() => new SecretClientFactory(_auth).Create(new Uri("https://v.vault.azure.net/")));
    }
}

[TestClass]
public class AzureResourceServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ArmClient _arm = Substitute.For<ArmClient>();
    private readonly IArmClientFactory _factory = Substitute.For<IArmClientFactory>();

    [TestInitialize]
    public void Setup() => _factory.Create().Returns(_arm);

    private static AsyncPageable<T> Pages<T>(params T[] items)
        where T : notnull =>
        AsyncPageable<T>.FromPages([Page<T>.FromValues(items, null, Substitute.For<Response>())]);

    private static TenantResource Tenant(Guid? id, string? name, string? domain)
    {
        var tenant = Substitute.For<TenantResource>();
        tenant.Data.Returns(ResourceManagerModelFactory.TenantData(tenantId: id, displayName: name, defaultDomain: domain));
        return tenant;
    }

    private static SubscriptionResource Subscription(string id, string name)
    {
        var subscription = Substitute.For<SubscriptionResource>();
        subscription.Data.Returns(ResourceManagerModelFactory.SubscriptionData(subscriptionId: id, displayName: name));
        return subscription;
    }

    [TestMethod]
    public async Task GetTenants_MapsAndSortsByLabel_SkippingEntriesWithoutId()
    {
        var pages = Pages(
            Tenant(TenantB, "Fabrikam", "fabrikam.com"),
            Tenant(null, "Broken", null),
            Tenant(TenantA, "Contoso", "contoso.onmicrosoft.com"));
        var collection = Substitute.For<TenantCollection>();
        collection.GetAllAsync(Arg.Any<CancellationToken>()).Returns(pages);
        _arm.GetTenants().Returns(collection);

        var tenants = await new AzureResourceService(_factory).GetTenantsAsync(CancellationToken.None);

        Assert.AreSequenceEqual(
            ["Contoso (contoso.onmicrosoft.com)", "Fabrikam (fabrikam.com)"],
            tenants.Select(t => t.DisplayName).ToArray());
        Assert.AreEqual(TenantA.ToString(), tenants[0].Id);
    }

    [TestMethod]
    public async Task GetSubscriptions_MapsAndSortsByName()
    {
        var pages = Pages(
            Subscription("s2", "Production"),
            Subscription("s1", "Dev"));
        var collection = Substitute.For<SubscriptionCollection>();
        collection.GetAllAsync(Arg.Any<CancellationToken>()).Returns(pages);
        _arm.GetSubscriptions().Returns(collection);

        var subscriptions = await new AzureResourceService(_factory).GetSubscriptionsAsync(CancellationToken.None);

        Assert.AreSequenceEqual(["Dev", "Production"], subscriptions.Select(s => s.DisplayName).ToArray());
        Assert.AreEqual("s1", subscriptions[0].Id);
    }

    [TestMethod]
    public async Task WhenSignedOut_TheFriendlyErrorSurfaces()
    {
        _factory.Create().Returns(_ => throw new ConfigFerryException("You are not signed in to Azure."));

        await Assert.ThrowsExactlyAsync<ConfigFerryException>(
            () => new AzureResourceService(_factory).GetSubscriptionsAsync(CancellationToken.None));
    }
}
