using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Azure;
using ConfigFerry.Core.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ConfigFerry.Core.Tests;

[TestClass]
public class KeyVaultReferenceTests
{
    [TestMethod]
    public void Parses_SecretUri_WithVersion()
    {
        Assert.IsTrue(KeyVaultReference.TryParse(
            "@Microsoft.KeyVault(SecretUri=https://myvault.vault.azure.net/secrets/db-pass/abc123)", out var r));
        Assert.AreEqual(new Uri("https://myvault.vault.azure.net/"), r!.VaultUri);
        Assert.AreEqual("db-pass", r.SecretName);
        Assert.AreEqual("abc123", r.Version);
    }

    [TestMethod]
    public void Parses_SecretUri_WithoutVersion()
    {
        Assert.IsTrue(KeyVaultReference.TryParse(
            "@Microsoft.KeyVault(SecretUri=https://myvault.vault.azure.net/secrets/db-pass)", out var r));
        Assert.IsNull(r!.Version);
    }

    [TestMethod]
    public void Parses_VaultNameForm_CaseInsensitive()
    {
        Assert.IsTrue(KeyVaultReference.TryParse(
            "  @microsoft.keyvault(vaultname=myvault; SecretName=s1; SecretVersion=v1)  ", out var r));
        Assert.AreEqual(new Uri("https://myvault.vault.azure.net/"), r!.VaultUri);
        Assert.AreEqual("s1", r.SecretName);
        Assert.AreEqual("v1", r.Version);
    }

    [TestMethod]
    public void Parses_SovereignCloudSecretUri()
    {
        Assert.IsTrue(KeyVaultReference.TryParse(
            "@Microsoft.KeyVault(SecretUri=https://v.vault.azure.cn/secrets/x/)", out var r));
        Assert.AreEqual("v.vault.azure.cn", r!.VaultUri.Host);
    }

    [TestMethod]
    [DataRow("plain value")]
    [DataRow(null)]
    [DataRow("@Microsoft.KeyVault(SecretUri=http://insecure/secrets/x)")]
    [DataRow("@Microsoft.KeyVault(SecretUri=https://v.vault.azure.net/keys/x)")]
    [DataRow("@Microsoft.KeyVault(VaultName=v)")]
    [DataRow("@Microsoft.KeyVault(garbage)")]
    public void Rejects_NonOrMalformedReferences(string? value) =>
        Assert.IsFalse(KeyVaultReference.TryParse(value, out _));

    [TestMethod]
    public void IsReference_TrueForMalformed_FalseForPlain()
    {
        Assert.IsTrue(KeyVaultReference.IsReference("@Microsoft.KeyVault(garbage)"));
        Assert.IsFalse(KeyVaultReference.IsReference("hello"));
    }
}

[TestClass]
public class KeyVaultReferenceResolverTests
{
    private const string RefA = "@Microsoft.KeyVault(SecretUri=https://v.vault.azure.net/secrets/a)";
    private const string RefB = "@Microsoft.KeyVault(VaultName=v;SecretName=b)";

    private readonly ISecretReader _reader = Substitute.For<ISecretReader>();

    private KeyVaultReferenceResolver Sut => new(_reader);

    [TestMethod]
    public async Task Resolves_SettingsAndConnectionStrings_ReadingEachDistinctSecretOnce()
    {
        _reader.GetSecretAsync(Arg.Any<Uri>(), "a", null, Arg.Any<CancellationToken>()).Returns("secret-a");
        _reader.GetSecretAsync(Arg.Any<Uri>(), "b", null, Arg.Any<CancellationToken>()).Returns("secret-b");

        var result = await Sut.ResolveAsync(
            new AzureAppConfiguration(
                new Dictionary<string, string> { ["One"] = RefA, ["Two"] = RefA, ["Plain"] = "p" },
                new Dictionary<string, string> { ["Db"] = RefB }),
            CancellationToken.None);

        Assert.AreEqual("secret-a", result.Configuration.AppSettings["One"]);
        Assert.AreEqual("secret-a", result.Configuration.AppSettings["Two"]);
        Assert.AreEqual("p", result.Configuration.AppSettings["Plain"]);
        Assert.AreEqual("secret-b", result.Configuration.ConnectionStrings["Db"]);
        Assert.AreEqual(3, result.Resolved);
        Assert.IsEmpty(result.Warnings);
        await _reader.Received(1).GetSecretAsync(Arg.Any<Uri>(), "a", null, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task FailedSecret_KeepsReference_AndWarns_OthersStillResolve()
    {
        _reader.GetSecretAsync(Arg.Any<Uri>(), "a", null, Arg.Any<CancellationToken>())
            .ThrowsAsync(new global::Azure.RequestFailedException(403, "forbidden"));
        _reader.GetSecretAsync(Arg.Any<Uri>(), "b", null, Arg.Any<CancellationToken>()).Returns("secret-b");

        var result = await Sut.ResolveAsync(
            new AzureAppConfiguration(
                new Dictionary<string, string> { ["One"] = RefA, ["Two"] = RefB },
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.AreEqual(RefA, result.Configuration.AppSettings["One"]);
        Assert.AreEqual("secret-b", result.Configuration.AppSettings["Two"]);
        Assert.AreEqual(1, result.Resolved);
        var warning = Assert.ContainsSingle(result.Warnings);
        Assert.Contains("access denied", warning, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task MalformedReference_IsKept_WithWarning()
    {
        var result = await Sut.ResolveAsync(
            new AzureAppConfiguration(
                new Dictionary<string, string> { ["Bad"] = "@Microsoft.KeyVault(garbage)" },
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.AreEqual("@Microsoft.KeyVault(garbage)", result.Configuration.AppSettings["Bad"]);
        Assert.Contains("Bad", Assert.ContainsSingle(result.Warnings));
        await _reader.DidNotReceiveWithAnyArgs().GetSecretAsync(default!, default!, default, default);
    }

    [TestMethod]
    public async Task Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Sut.ResolveAsync(
            new AzureAppConfiguration(
                new Dictionary<string, string> { ["One"] = RefA },
                new Dictionary<string, string>()),
            cts.Token));
    }
}
