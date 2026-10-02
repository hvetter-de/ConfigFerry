using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Azure;
using ConfigFerry.Core.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ConfigFerry.Core.Tests;

public class KeyVaultReferenceTests
{
    [Fact]
    public void Parses_SecretUri_WithVersion()
    {
        Assert.True(KeyVaultReference.TryParse(
            "@Microsoft.KeyVault(SecretUri=https://myvault.vault.azure.net/secrets/db-pass/abc123)", out var r));
        Assert.Equal(new Uri("https://myvault.vault.azure.net/"), r!.VaultUri);
        Assert.Equal("db-pass", r.SecretName);
        Assert.Equal("abc123", r.Version);
    }

    [Fact]
    public void Parses_SecretUri_WithoutVersion()
    {
        Assert.True(KeyVaultReference.TryParse(
            "@Microsoft.KeyVault(SecretUri=https://myvault.vault.azure.net/secrets/db-pass)", out var r));
        Assert.Null(r!.Version);
    }

    [Fact]
    public void Parses_VaultNameForm_CaseInsensitive()
    {
        Assert.True(KeyVaultReference.TryParse(
            "  @microsoft.keyvault(vaultname=myvault; SecretName=s1; SecretVersion=v1)  ", out var r));
        Assert.Equal(new Uri("https://myvault.vault.azure.net/"), r!.VaultUri);
        Assert.Equal("s1", r.SecretName);
        Assert.Equal("v1", r.Version);
    }

    [Fact]
    public void Parses_SovereignCloudSecretUri()
    {
        Assert.True(KeyVaultReference.TryParse(
            "@Microsoft.KeyVault(SecretUri=https://v.vault.azure.cn/secrets/x/)", out var r));
        Assert.Equal("v.vault.azure.cn", r!.VaultUri.Host);
    }

    [Theory]
    [InlineData("plain value")]
    [InlineData(null)]
    [InlineData("@Microsoft.KeyVault(SecretUri=http://insecure/secrets/x)")]
    [InlineData("@Microsoft.KeyVault(SecretUri=https://v.vault.azure.net/keys/x)")]
    [InlineData("@Microsoft.KeyVault(VaultName=v)")]
    [InlineData("@Microsoft.KeyVault(garbage)")]
    public void Rejects_NonOrMalformedReferences(string? value) =>
        Assert.False(KeyVaultReference.TryParse(value, out _));

    [Fact]
    public void IsReference_TrueForMalformed_FalseForPlain()
    {
        Assert.True(KeyVaultReference.IsReference("@Microsoft.KeyVault(garbage)"));
        Assert.False(KeyVaultReference.IsReference("hello"));
    }
}

public class KeyVaultReferenceResolverTests
{
    private const string RefA = "@Microsoft.KeyVault(SecretUri=https://v.vault.azure.net/secrets/a)";
    private const string RefB = "@Microsoft.KeyVault(VaultName=v;SecretName=b)";

    private readonly ISecretReader _reader = Substitute.For<ISecretReader>();

    private KeyVaultReferenceResolver Sut => new(_reader);

    [Fact]
    public async Task Resolves_SettingsAndConnectionStrings_ReadingEachDistinctSecretOnce()
    {
        _reader.GetSecretAsync(Arg.Any<Uri>(), "a", null, Arg.Any<CancellationToken>()).Returns("secret-a");
        _reader.GetSecretAsync(Arg.Any<Uri>(), "b", null, Arg.Any<CancellationToken>()).Returns("secret-b");

        var result = await Sut.ResolveAsync(
            new AzureAppConfiguration(
                new Dictionary<string, string> { ["One"] = RefA, ["Two"] = RefA, ["Plain"] = "p" },
                new Dictionary<string, string> { ["Db"] = RefB }),
            CancellationToken.None);

        Assert.Equal("secret-a", result.Configuration.AppSettings["One"]);
        Assert.Equal("secret-a", result.Configuration.AppSettings["Two"]);
        Assert.Equal("p", result.Configuration.AppSettings["Plain"]);
        Assert.Equal("secret-b", result.Configuration.ConnectionStrings["Db"]);
        Assert.Equal(3, result.Resolved);
        Assert.Empty(result.Warnings);
        await _reader.Received(1).GetSecretAsync(Arg.Any<Uri>(), "a", null, Arg.Any<CancellationToken>());
    }

    [Fact]
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

        Assert.Equal(RefA, result.Configuration.AppSettings["One"]);
        Assert.Equal("secret-b", result.Configuration.AppSettings["Two"]);
        Assert.Equal(1, result.Resolved);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("access denied", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedReference_IsKept_WithWarning()
    {
        var result = await Sut.ResolveAsync(
            new AzureAppConfiguration(
                new Dictionary<string, string> { ["Bad"] = "@Microsoft.KeyVault(garbage)" },
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.Equal("@Microsoft.KeyVault(garbage)", result.Configuration.AppSettings["Bad"]);
        Assert.Contains("Bad", Assert.Single(result.Warnings));
        await _reader.DidNotReceiveWithAnyArgs().GetSecretAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sut.ResolveAsync(
            new AzureAppConfiguration(
                new Dictionary<string, string> { ["One"] = RefA },
                new Dictionary<string, string>()),
            cts.Token));
    }
}
