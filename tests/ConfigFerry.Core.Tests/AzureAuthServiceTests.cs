using Azure.Core;
using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Azure;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ConfigFerry.Core.Tests;

[TestClass]
public class AzureAuthServiceTests
{
    private const string HomeTenant = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenant = "22222222-2222-2222-2222-222222222222";

    private readonly IAuthSessionFactory _factory = Substitute.For<IAuthSessionFactory>();
    private readonly AzureAuthService _sut;

    public AzureAuthServiceTests() => _sut = new AzureAuthService(_factory);

    private static AuthSession Session(string tenant, string user = "jane@contoso.com") =>
        new(Substitute.For<TokenCredential>(), user, tenant);

    private async Task SignInAtHomeAsync()
    {
        _factory.SignInAsync("default", null, Arg.Any<CancellationToken>()).Returns(Session(HomeTenant));
        await _sut.SignInAsync(CancellationToken.None);
    }

    [TestMethod]
    public void NewService_IsSignedOut_AndHasNoCredential()
    {
        Assert.IsFalse(_sut.IsSignedIn);
        Assert.IsNull(_sut.AccountName);
        Assert.IsNull(_sut.TenantId);
        Assert.ThrowsExactly<ConfigFerryException>(() => _sut.GetCredential());
    }

    [TestMethod]
    public async Task SignIn_UsesDefaultSlot_AndExposesSession()
    {
        var session = Session(HomeTenant);
        _factory.SignInAsync("default", null, Arg.Any<CancellationToken>()).Returns(session);

        await _sut.SignInAsync(CancellationToken.None);

        Assert.IsTrue(_sut.IsSignedIn);
        Assert.AreEqual("jane@contoso.com", _sut.AccountName);
        Assert.AreEqual(HomeTenant, _sut.TenantId);
        Assert.AreSame(session.Credential, _sut.GetCredential());
    }

    [TestMethod]
    public async Task SilentSignIn_WithPersistedSession_SignsIn()
    {
        _factory.TryRestoreAsync("default", null, Arg.Any<CancellationToken>()).Returns(Session(HomeTenant));

        var restored = await _sut.TrySignInSilentlyAsync(CancellationToken.None);

        Assert.IsTrue(restored);
        Assert.IsTrue(_sut.IsSignedIn);
        await _factory.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default);
    }

    [TestMethod]
    public async Task SilentSignIn_WithoutPersistedSession_StaysSignedOut()
    {
        _factory.TryRestoreAsync("default", null, Arg.Any<CancellationToken>()).Returns((AuthSession?)null);

        Assert.IsFalse(await _sut.TrySignInSilentlyAsync(CancellationToken.None));
        Assert.IsFalse(_sut.IsSignedIn);
    }

    [TestMethod]
    public async Task SignIn_Failure_LeavesServiceSignedOut()
    {
        _factory.SignInAsync("default", null, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("cancelled"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _sut.SignInAsync(CancellationToken.None));

        Assert.IsFalse(_sut.IsSignedIn);
    }

    [TestMethod]
    public async Task SwitchTenant_PrefersCachedSession_InTenantSlot_WithoutPrompt()
    {
        await SignInAtHomeAsync();
        var other = Session(OtherTenant);
        _factory.TryRestoreAsync(OtherTenant, OtherTenant, Arg.Any<CancellationToken>()).Returns(other);

        await _sut.SwitchTenantAsync(OtherTenant, CancellationToken.None);

        Assert.AreEqual(OtherTenant, _sut.TenantId);
        Assert.AreSame(other.Credential, _sut.GetCredential());
        await _factory.DidNotReceive().SignInAsync(OtherTenant, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task SwitchTenant_WithoutCachedSession_SignsInInteractively()
    {
        await SignInAtHomeAsync();
        _factory.TryRestoreAsync(OtherTenant, OtherTenant, Arg.Any<CancellationToken>()).Returns((AuthSession?)null);
        _factory.SignInAsync(OtherTenant, OtherTenant, Arg.Any<CancellationToken>()).Returns(Session(OtherTenant));

        await _sut.SwitchTenantAsync(OtherTenant, CancellationToken.None);

        Assert.AreEqual(OtherTenant, _sut.TenantId);
        await _factory.Received(1).SignInAsync(OtherTenant, OtherTenant, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task SwitchTenant_BackToHome_UsesDefaultSlot()
    {
        await SignInAtHomeAsync();
        _factory.TryRestoreAsync(OtherTenant, OtherTenant, Arg.Any<CancellationToken>()).Returns(Session(OtherTenant));
        await _sut.SwitchTenantAsync(OtherTenant, CancellationToken.None);
        _factory.TryRestoreAsync("default", HomeTenant, Arg.Any<CancellationToken>()).Returns(Session(HomeTenant));

        await _sut.SwitchTenantAsync(HomeTenant, CancellationToken.None);

        Assert.AreEqual(HomeTenant, _sut.TenantId);
        await _factory.Received(1).TryRestoreAsync("default", HomeTenant, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task SwitchTenant_ToCurrentTenant_IsNoOp()
    {
        await SignInAtHomeAsync();
        _factory.ClearReceivedCalls();

        await _sut.SwitchTenantAsync(HomeTenant.ToUpperInvariant(), CancellationToken.None);

        Assert.AreEqual(0, _factory.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task SwitchTenant_Failure_KeepsPreviousSession()
    {
        await SignInAtHomeAsync();
        var homeCredential = _sut.GetCredential();
        _factory.TryRestoreAsync(OtherTenant, OtherTenant, Arg.Any<CancellationToken>()).Returns((AuthSession?)null);
        _factory.SignInAsync(OtherTenant, OtherTenant, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("denied"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _sut.SwitchTenantAsync(OtherTenant, CancellationToken.None));

        Assert.AreEqual(HomeTenant, _sut.TenantId);
        Assert.AreSame(homeCredential, _sut.GetCredential());
    }

    [TestMethod]
    public async Task SwitchTenant_RequiresSignIn_AndAGuid()
    {
        await Assert.ThrowsExactlyAsync<ConfigFerryException>(() => _sut.SwitchTenantAsync(OtherTenant, CancellationToken.None));

        await SignInAtHomeAsync();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _sut.SwitchTenantAsync("../../etc", CancellationToken.None));
    }

    [TestMethod]
    public async Task SignOut_ClearsSession_AndForgetsPersistedSessions()
    {
        await SignInAtHomeAsync();

        _sut.SignOut();

        Assert.IsFalse(_sut.IsSignedIn);
        Assert.IsNull(_sut.TenantId);
        _factory.Received(1).ForgetAll();
        Assert.ThrowsExactly<ConfigFerryException>(() => _sut.GetCredential());
    }
}
