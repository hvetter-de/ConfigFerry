using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Models;
using ConfigFerry.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ConfigFerry.Core.Tests;

[TestClass]
public sealed class MainViewModelTests : IDisposable
{
    private const string FilePath = @"C:\x\appsettings.json";

    private static readonly TenantInfo TenantA = new("11111111-1111-1111-1111-111111111111", "Contoso");
    private static readonly TenantInfo TenantB = new("22222222-2222-2222-2222-222222222222", "Fabrikam");
    private static readonly SubscriptionInfo Sub1 = new("s1", "Sub One");
    private static readonly SubscriptionInfo Sub2 = new("s2", "Sub Two");
    private static readonly AppServiceInfo Web = new("/r/web", "web", "rg", AppServiceKind.WebApp);
    private static readonly AppServiceInfo Func = new("/r/func", "func", "rg", AppServiceKind.FunctionApp);

    private readonly IAzureAuthService _auth = Substitute.For<IAzureAuthService>();
    private readonly IAzureResourceService _azure = Substitute.For<IAzureResourceService>();
    private readonly IKeyVaultReferenceResolver _resolver = Substitute.For<IKeyVaultReferenceResolver>();
    private readonly IConfigGenerator _generator = Substitute.For<IConfigGenerator>();
    private readonly IFilePickerService _picker = Substitute.For<IFilePickerService>();
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();
    private readonly ITextFileStore _files = Substitute.For<ITextFileStore>();
    private readonly MainViewModel _sut;

    private readonly AzureAppConfiguration _config = new(
        new Dictionary<string, string> { ["A"] = "1" }, new Dictionary<string, string>());

    public MainViewModelTests()
    {
        _auth.AccountName.Returns("me@contoso.com");
        _auth.TenantId.Returns(TenantA.Id);
        _azure.GetTenantsAsync(Arg.Any<CancellationToken>()).Returns([TenantA, TenantB]);
        _azure.GetSubscriptionsAsync(Arg.Any<CancellationToken>()).Returns([Sub1, Sub2]);
        _azure.GetAppServicesAsync("s1", Arg.Any<CancellationToken>()).Returns([Web, Func]);
        _azure.GetConfigurationAsync(Arg.Any<AppServiceInfo>(), Arg.Any<CancellationToken>()).Returns(_config);
        _resolver.ResolveAsync(Arg.Any<AzureAppConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(call => new KeyVaultResolutionResult(call.Arg<AzureAppConfiguration>(), 0, []));
        _generator.Generate(Arg.Any<AzureAppConfiguration>(), Arg.Any<GenerationOptions>(), Arg.Any<string?>())
            .Returns(new GenerationResult("{\"generated\":true}", new MergeStats(2, 1, 0), []));

        _sut = new MainViewModel(_auth, _azure, _resolver, _generator, _picker, _clipboard, _files, NullLogger<MainViewModel>.Instance);
    }

    public void Dispose() => _sut.Dispose();

    private async Task SignInAndSelectAsync(AppServiceInfo app)
    {
        await _sut.SignInCommand.ExecuteAsync(null);
        _sut.SelectedSubscription = Sub1;
        await WaitForAppServicesAsync();
        _sut.SelectedAppService = app;
    }

    private async Task WaitForAppServicesAsync()
    {
        for (var i = 0; i < 200 && (_sut.IsLoadingAppServices || _sut.AppServices.Count == 0); i++)
        {
            await Task.Delay(10);
        }
    }

    private async Task SelectFileTargetAsync(string existingContent)
    {
        _files.Exists(FilePath).Returns(true);
        _files.ReadAllTextAsync(FilePath, Arg.Any<CancellationToken>()).Returns(existingContent);
        await SignInAndSelectAsync(Web);
        _sut.IsFileTarget = true;
        _sut.SelectedFilePath = FilePath;
    }

    [TestMethod]
    public async Task SignIn_LoadsSubscriptions_AndSetsAccount()
    {
        await _sut.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(_sut.IsSignedIn);
        Assert.AreEqual("me@contoso.com", _sut.AccountName);
        Assert.AreSequenceEqual([Sub1, Sub2], _sut.Subscriptions);
    }

    [TestMethod]
    public async Task SignIn_Failure_ShowsError_AndStaysSignedOut()
    {
        _auth.SignInAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("nope"));

        await _sut.SignInCommand.ExecuteAsync(null);

        Assert.IsFalse(_sut.IsSignedIn);
        Assert.AreEqual(StatusSeverity.Error, _sut.StatusSeverity);
        Assert.Contains("nope", _sut.StatusMessage!);
    }

    [TestMethod]
    public async Task Initialize_RestoresSessionSilently()
    {
        _auth.TrySignInSilentlyAsync(Arg.Any<CancellationToken>()).Returns(true);

        await _sut.InitializeAsync();

        Assert.IsTrue(_sut.IsSignedIn);
        Assert.AreEqual(2, _sut.Subscriptions.Count);
    }

    [TestMethod]
    public async Task Initialize_WithoutSession_StaysSignedOut()
    {
        _auth.TrySignInSilentlyAsync(Arg.Any<CancellationToken>()).Returns(false);

        await _sut.InitializeAsync();

        Assert.IsFalse(_sut.IsSignedIn);
    }

    [TestMethod]
    public async Task SingleSubscription_IsPreselected()
    {
        _azure.GetSubscriptionsAsync(Arg.Any<CancellationToken>()).Returns([Sub1]);

        await _sut.SignInCommand.ExecuteAsync(null);
        await WaitForAppServicesAsync();

        Assert.AreEqual(Sub1, _sut.SelectedSubscription);
        Assert.AreEqual(2, _sut.AppServices.Count);
    }

    [TestMethod]
    public async Task SelectingFunctionApp_SuggestsFunctionsFormat()
    {
        await SignInAndSelectAsync(Func);
        Assert.IsTrue(_sut.IsFunctionsFormat);

        _sut.SelectedAppService = Web;
        Assert.IsFalse(_sut.IsFunctionsFormat);
    }

    [TestMethod]
    public async Task SignOut_ClearsEverything()
    {
        await SignInAndSelectAsync(Web);

        _sut.SignOutCommand.Execute(null);

        Assert.IsFalse(_sut.IsSignedIn);
        Assert.IsEmpty(_sut.Subscriptions);
        Assert.IsEmpty(_sut.AppServices);
        Assert.IsNull(_sut.SelectedAppService);
        _auth.Received(1).SignOut();
    }

    [TestMethod]
    public async Task Generate_JsonTarget_ProducesPreview_WithoutReadingFiles()
    {
        await SignInAndSelectAsync(Func);

        await _sut.GenerateCommand.ExecuteAsync(null);

        Assert.AreEqual("{\"generated\":true}", _sut.Preview);
        Assert.Contains("2 added", _sut.Summary!);
        _generator.Received(1).Generate(
            _config,
            Arg.Is<GenerationOptions>(o => o.Format == ConfigFormat.FunctionLocalSettings && o.ExcludePlatformSettings),
            null);
        _files.DidNotReceiveWithAnyArgs().Exists(default!);
    }

    [TestMethod]
    public async Task Generate_ResolvesKeyVault_AndPassesResolvedConfigToGenerator()
    {
        var resolved = new AzureAppConfiguration(
            new Dictionary<string, string> { ["A"] = "secret" }, new Dictionary<string, string>());
        _resolver.ResolveAsync(_config, Arg.Any<CancellationToken>())
            .Returns(new KeyVaultResolutionResult(resolved, 1, ["Could not read X"]));
        await SignInAndSelectAsync(Web);

        await _sut.GenerateCommand.ExecuteAsync(null);

        _generator.Received(1).Generate(resolved, Arg.Any<GenerationOptions>(), Arg.Any<string?>());
        Assert.Contains("Could not read X", _sut.Warnings);
        Assert.AreEqual(StatusSeverity.Warning, _sut.StatusSeverity);
    }

    [TestMethod]
    public async Task Generate_WithKeyVaultResolutionOff_SkipsResolver_AndWarnsAboutReferences()
    {
        _azure.GetConfigurationAsync(Arg.Any<AppServiceInfo>(), Arg.Any<CancellationToken>()).Returns(
            new AzureAppConfiguration(
                new Dictionary<string, string> { ["S"] = "@Microsoft.KeyVault(VaultName=v;SecretName=s)" },
                new Dictionary<string, string>()));
        await SignInAndSelectAsync(Web);
        _sut.ResolveKeyVaultReferences = false;

        await _sut.GenerateCommand.ExecuteAsync(null);

        await _resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
        Assert.Contains(w => w.Contains("Key Vault", StringComparison.Ordinal), _sut.Warnings);
    }

    [TestMethod]
    public async Task Generate_FileTarget_RequiresFile()
    {
        await SignInAndSelectAsync(Web);
        _sut.IsFileTarget = true;

        Assert.IsFalse(_sut.GenerateCommand.CanExecute(null));

        _sut.SelectedFilePath = FilePath;
        Assert.IsTrue(_sut.GenerateCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task Generate_FileTarget_MergesIntoFileContent()
    {
        await SelectFileTargetAsync("{\"local\":1}");

        await _sut.GenerateCommand.ExecuteAsync(null);

        _generator.Received(1).Generate(_config, Arg.Any<GenerationOptions>(), "{\"local\":1}");
        Assert.IsTrue(_sut.SaveToFileCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task Generate_MissingFile_ShowsError()
    {
        _files.Exists(Arg.Any<string>()).Returns(false);
        await SignInAndSelectAsync(Web);
        _sut.IsFileTarget = true;
        _sut.SelectedFilePath = @"C:\gone.json";

        await _sut.GenerateCommand.ExecuteAsync(null);

        Assert.AreEqual(StatusSeverity.Error, _sut.StatusSeverity);
        Assert.IsEmpty(_sut.Preview);
    }

    [TestMethod]
    public async Task Generate_GeneratorFailure_ShowsError()
    {
        _generator.Generate(Arg.Any<AzureAppConfiguration>(), Arg.Any<GenerationOptions>(), Arg.Any<string?>())
            .Returns(_ => throw new ConfigFerryException("bad file"));
        await SignInAndSelectAsync(Web);

        await _sut.GenerateCommand.ExecuteAsync(null);

        Assert.AreEqual(StatusSeverity.Error, _sut.StatusSeverity);
        Assert.Contains("bad file", _sut.StatusMessage!);
    }

    [TestMethod]
    public async Task Save_WritesPreview_WithBackup()
    {
        await SelectFileTargetAsync("{}");
        await _sut.GenerateCommand.ExecuteAsync(null);

        await _sut.SaveToFileCommand.ExecuteAsync(null);

        _files.Received(1).Copy(FilePath, FilePath + ".bak", true);
        await _files.Received(1).WriteAllTextAsync(FilePath, "{\"generated\":true}", Arg.Any<CancellationToken>());
        Assert.AreEqual(StatusSeverity.Success, _sut.StatusSeverity);
    }

    [TestMethod]
    public async Task Save_WithoutBackupOption_DoesNotCopy()
    {
        await SelectFileTargetAsync("{}");
        _sut.CreateBackup = false;
        await _sut.GenerateCommand.ExecuteAsync(null);

        await _sut.SaveToFileCommand.ExecuteAsync(null);

        _files.DidNotReceiveWithAnyArgs().Copy(default!, default!, default);
    }

    [TestMethod]
    public async Task Save_RefusesWhenFileChangedSincePreview()
    {
        await SelectFileTargetAsync("{}");
        _files.ReadAllTextAsync(FilePath, Arg.Any<CancellationToken>()).Returns("{}", "{\"edited\":true}");
        await _sut.GenerateCommand.ExecuteAsync(null);

        await _sut.SaveToFileCommand.ExecuteAsync(null);

        await _files.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        Assert.AreEqual(StatusSeverity.Error, _sut.StatusSeverity);
    }

    [TestMethod]
    public async Task Copy_PutsPreviewOnClipboard()
    {
        await SignInAndSelectAsync(Web);
        Assert.IsFalse(_sut.CopyToClipboardCommand.CanExecute(null));
        await _sut.GenerateCommand.ExecuteAsync(null);

        _sut.CopyToClipboardCommand.Execute(null);

        _clipboard.Received(1).SetText("{\"generated\":true}");
    }

    [TestMethod]
    public async Task ChangingOptions_InvalidatesPreview()
    {
        await SignInAndSelectAsync(Web);
        await _sut.GenerateCommand.ExecuteAsync(null);
        Assert.IsNotEmpty(_sut.Preview);

        _sut.IsFunctionsFormat = !_sut.IsFunctionsFormat;

        Assert.IsEmpty(_sut.Preview);
        Assert.IsFalse(_sut.CopyToClipboardCommand.CanExecute(null));
    }

    [TestMethod]
    [DataRow(@"C:\p\local.settings.json", true)]
    [DataRow(@"C:\p\appsettings.Development.json", false)]
    public async Task Browse_PicksFormatFromFileName(string path, bool expectedFunctions)
    {
        _picker.PickSettingsFileAsync().Returns(path);
        _sut.IsFunctionsFormat = !expectedFunctions;

        await _sut.BrowseFileCommand.ExecuteAsync(null);

        Assert.AreEqual(path, _sut.SelectedFilePath);
        Assert.AreEqual(expectedFunctions, _sut.IsFunctionsFormat);
    }

    [TestMethod]
    public async Task Browse_Cancelled_KeepsPath()
    {
        _picker.PickSettingsFileAsync().Returns((string?)null);
        _sut.SelectedFilePath = "keep";

        await _sut.BrowseFileCommand.ExecuteAsync(null);

        Assert.AreEqual("keep", _sut.SelectedFilePath);
    }

    [TestMethod]
    public async Task SelectingAppService_EnablesGenerate_AndRaisesCanExecuteChanged()
    {
        await _sut.SignInCommand.ExecuteAsync(null);
        _sut.SelectedSubscription = Sub1;
        await WaitForAppServicesAsync();
        Assert.IsFalse(_sut.GenerateCommand.CanExecute(null));
        var raised = 0;
        _sut.GenerateCommand.CanExecuteChanged += (_, _) => raised++;

        _sut.SelectedAppService = Web;

        Assert.IsTrue(raised > 0);
        Assert.IsTrue(_sut.GenerateCommand.CanExecute(null));
    }

    [TestMethod]
    public void FormatSelection_IsInverseOfFunctionsFormat()
    {
        Assert.IsTrue(_sut.IsAppSettingsFormat);
        _sut.IsFunctionsFormat = true;
        Assert.IsFalse(_sut.IsAppSettingsFormat);
        _sut.IsAppSettingsFormat = true;
        Assert.IsFalse(_sut.IsFunctionsFormat);
    }

    [TestMethod]
    public async Task SignIn_LoadsTenants_AndSelectsCurrentOne_WithoutSwitching()
    {
        await _sut.SignInCommand.ExecuteAsync(null);

        Assert.AreSequenceEqual([TenantA, TenantB], _sut.Tenants);
        Assert.AreEqual(TenantA, _sut.SelectedTenant);
        await _auth.DidNotReceiveWithAnyArgs().SwitchTenantAsync(default!, default);
    }

    [TestMethod]
    public async Task SignIn_TenantListFailure_IsNotFatal()
    {
        _azure.GetTenantsAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("no tenants"));

        await _sut.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(_sut.IsSignedIn);
        Assert.IsEmpty(_sut.Tenants);
        Assert.AreEqual(2, _sut.Subscriptions.Count);
    }

    [TestMethod]
    public async Task SelectingOtherTenant_SwitchesAndReloadsSubscriptions()
    {
        await SignInAndSelectAsync(Web);
        var tenantBSubs = new SubscriptionInfo("s9", "Fabrikam Sub");
        _azure.GetSubscriptionsAsync(Arg.Any<CancellationToken>()).Returns([tenantBSubs]);
        _auth.When(a => a.SwitchTenantAsync(TenantB.Id, Arg.Any<CancellationToken>()))
            .Do(_ => _auth.TenantId.Returns(TenantB.Id));

        _sut.SelectedTenant = TenantB;
        await WaitUntilAsync(() => !_sut.IsBusy && _sut.Subscriptions.Contains(tenantBSubs));

        await _auth.Received(1).SwitchTenantAsync(TenantB.Id, Arg.Any<CancellationToken>());
        Assert.AreSequenceEqual([tenantBSubs], _sut.Subscriptions);
        Assert.AreEqual(TenantB, _sut.SelectedTenant);
        Assert.IsNull(_sut.SelectedAppService);
    }

    [TestMethod]
    public async Task FailedTenantSwitch_ShowsError_AndRevertsSelection()
    {
        await _sut.SignInCommand.ExecuteAsync(null);
        _auth.SwitchTenantAsync(TenantB.Id, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("login cancelled"));

        _sut.SelectedTenant = TenantB;
        await WaitUntilAsync(() => !_sut.IsBusy && _sut.StatusSeverity == StatusSeverity.Error);

        Assert.AreEqual(TenantA, _sut.SelectedTenant);
        Assert.Contains("login cancelled", _sut.StatusMessage!);
        Assert.AreEqual(2, _sut.Subscriptions.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    [TestMethod]
    [DataRow("jane.doe@contoso.com", "JA")]
    [DataRow("x@y.z", "X")]
    [DataRow("42@y.z", "?")]
    [DataRow(null, "?")]
    public void AccountInitials_AreDerivedFromUserName(string? account, string expected)
    {
        _sut.AccountName = account;

        Assert.AreEqual(expected, _sut.AccountInitials);
    }

    [TestMethod]
    public async Task PreviewTitle_FollowsFormatAndFile()
    {
        Assert.AreEqual("appsettings.json", _sut.PreviewTitle);

        _sut.IsFunctionsFormat = true;
        Assert.AreEqual("local.settings.json", _sut.PreviewTitle);

        _sut.IsFileTarget = true;
        _sut.SelectedFilePath = @"C:\p\appsettings.Development.json";
        Assert.AreEqual("appsettings.Development.json", _sut.PreviewTitle);

        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task Generate_PublishesCountsAndPreviewFlags()
    {
        await SignInAndSelectAsync(Web);
        Assert.IsTrue(_sut.IsPreviewEmpty);

        await _sut.GenerateCommand.ExecuteAsync(null);

        Assert.IsTrue(_sut.HasPreviewText);
        Assert.IsFalse(_sut.IsPreviewEmpty);
        Assert.AreEqual((2, 1, 0), (_sut.AddedCount, _sut.OverriddenCount, _sut.UnchangedCount));

        _sut.IsFunctionsFormat = true; // invalidates the result

        Assert.IsTrue(_sut.IsPreviewEmpty);
        Assert.AreEqual((0, 0, 0), (_sut.AddedCount, _sut.OverriddenCount, _sut.UnchangedCount));
    }

    [TestMethod]
    public async Task Generate_WithWarnings_MentionsThemInStatus()
    {
        _resolver.ResolveAsync(Arg.Any<AzureAppConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(call => new KeyVaultResolutionResult(call.Arg<AzureAppConfiguration>(), 0, ["w1", "w2"]));
        await SignInAndSelectAsync(Web);

        await _sut.GenerateCommand.ExecuteAsync(null);

        Assert.Contains("2 warning(s)", _sut.StatusMessage!);
    }

    [TestMethod]
    public void JsonTarget_IsInverseOfFileTarget()
    {
        Assert.IsTrue(_sut.IsJsonTarget);
        _sut.IsFileTarget = true;
        Assert.IsFalse(_sut.IsJsonTarget);
        _sut.IsJsonTarget = true;
        Assert.IsFalse(_sut.IsFileTarget);
    }
}
