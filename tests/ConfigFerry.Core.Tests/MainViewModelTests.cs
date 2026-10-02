using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Models;
using ConfigFerry.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ConfigFerry.Core.Tests;

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

    [Fact]
    public async Task SignIn_LoadsSubscriptions_AndSetsAccount()
    {
        await _sut.SignInCommand.ExecuteAsync(null);

        Assert.True(_sut.IsSignedIn);
        Assert.Equal("me@contoso.com", _sut.AccountName);
        Assert.Equal([Sub1, Sub2], _sut.Subscriptions);
    }

    [Fact]
    public async Task SignIn_Failure_ShowsError_AndStaysSignedOut()
    {
        _auth.SignInAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("nope"));

        await _sut.SignInCommand.ExecuteAsync(null);

        Assert.False(_sut.IsSignedIn);
        Assert.Equal(StatusSeverity.Error, _sut.StatusSeverity);
        Assert.Contains("nope", _sut.StatusMessage);
    }

    [Fact]
    public async Task Initialize_RestoresSessionSilently()
    {
        _auth.TrySignInSilentlyAsync(Arg.Any<CancellationToken>()).Returns(true);

        await _sut.InitializeAsync();

        Assert.True(_sut.IsSignedIn);
        Assert.Equal(2, _sut.Subscriptions.Count);
    }

    [Fact]
    public async Task Initialize_WithoutSession_StaysSignedOut()
    {
        _auth.TrySignInSilentlyAsync(Arg.Any<CancellationToken>()).Returns(false);

        await _sut.InitializeAsync();

        Assert.False(_sut.IsSignedIn);
    }

    [Fact]
    public async Task SingleSubscription_IsPreselected()
    {
        _azure.GetSubscriptionsAsync(Arg.Any<CancellationToken>()).Returns([Sub1]);

        await _sut.SignInCommand.ExecuteAsync(null);
        await WaitForAppServicesAsync();

        Assert.Equal(Sub1, _sut.SelectedSubscription);
        Assert.Equal(2, _sut.AppServices.Count);
    }

    [Fact]
    public async Task SelectingFunctionApp_SuggestsFunctionsFormat()
    {
        await SignInAndSelectAsync(Func);
        Assert.True(_sut.IsFunctionsFormat);

        _sut.SelectedAppService = Web;
        Assert.False(_sut.IsFunctionsFormat);
    }

    [Fact]
    public async Task SignOut_ClearsEverything()
    {
        await SignInAndSelectAsync(Web);

        _sut.SignOutCommand.Execute(null);

        Assert.False(_sut.IsSignedIn);
        Assert.Empty(_sut.Subscriptions);
        Assert.Empty(_sut.AppServices);
        Assert.Null(_sut.SelectedAppService);
        _auth.Received(1).SignOut();
    }

    [Fact]
    public async Task Generate_JsonTarget_ProducesPreview_WithoutReadingFiles()
    {
        await SignInAndSelectAsync(Func);

        await _sut.GenerateCommand.ExecuteAsync(null);

        Assert.Equal("{\"generated\":true}", _sut.Preview);
        Assert.Contains("2 added", _sut.Summary);
        _generator.Received(1).Generate(
            _config,
            Arg.Is<GenerationOptions>(o => o.Format == ConfigFormat.FunctionLocalSettings && o.ExcludePlatformSettings),
            null);
        _files.DidNotReceiveWithAnyArgs().Exists(default!);
    }

    [Fact]
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
        Assert.Equal(StatusSeverity.Warning, _sut.StatusSeverity);
    }

    [Fact]
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
        Assert.Contains(_sut.Warnings, w => w.Contains("Key Vault", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generate_FileTarget_RequiresFile()
    {
        await SignInAndSelectAsync(Web);
        _sut.IsFileTarget = true;

        Assert.False(_sut.GenerateCommand.CanExecute(null));

        _sut.SelectedFilePath = FilePath;
        Assert.True(_sut.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Generate_FileTarget_MergesIntoFileContent()
    {
        await SelectFileTargetAsync("{\"local\":1}");

        await _sut.GenerateCommand.ExecuteAsync(null);

        _generator.Received(1).Generate(_config, Arg.Any<GenerationOptions>(), "{\"local\":1}");
        Assert.True(_sut.SaveToFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task Generate_MissingFile_ShowsError()
    {
        _files.Exists(Arg.Any<string>()).Returns(false);
        await SignInAndSelectAsync(Web);
        _sut.IsFileTarget = true;
        _sut.SelectedFilePath = @"C:\gone.json";

        await _sut.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(StatusSeverity.Error, _sut.StatusSeverity);
        Assert.Empty(_sut.Preview);
    }

    [Fact]
    public async Task Generate_GeneratorFailure_ShowsError()
    {
        _generator.Generate(Arg.Any<AzureAppConfiguration>(), Arg.Any<GenerationOptions>(), Arg.Any<string?>())
            .Returns(_ => throw new ConfigFerryException("bad file"));
        await SignInAndSelectAsync(Web);

        await _sut.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(StatusSeverity.Error, _sut.StatusSeverity);
        Assert.Contains("bad file", _sut.StatusMessage);
    }

    [Fact]
    public async Task Save_WritesPreview_WithBackup()
    {
        await SelectFileTargetAsync("{}");
        await _sut.GenerateCommand.ExecuteAsync(null);

        await _sut.SaveToFileCommand.ExecuteAsync(null);

        _files.Received(1).Copy(FilePath, FilePath + ".bak", true);
        await _files.Received(1).WriteAllTextAsync(FilePath, "{\"generated\":true}", Arg.Any<CancellationToken>());
        Assert.Equal(StatusSeverity.Success, _sut.StatusSeverity);
    }

    [Fact]
    public async Task Save_WithoutBackupOption_DoesNotCopy()
    {
        await SelectFileTargetAsync("{}");
        _sut.CreateBackup = false;
        await _sut.GenerateCommand.ExecuteAsync(null);

        await _sut.SaveToFileCommand.ExecuteAsync(null);

        _files.DidNotReceiveWithAnyArgs().Copy(default!, default!, default);
    }

    [Fact]
    public async Task Save_RefusesWhenFileChangedSincePreview()
    {
        await SelectFileTargetAsync("{}");
        _files.ReadAllTextAsync(FilePath, Arg.Any<CancellationToken>()).Returns("{}", "{\"edited\":true}");
        await _sut.GenerateCommand.ExecuteAsync(null);

        await _sut.SaveToFileCommand.ExecuteAsync(null);

        await _files.DidNotReceiveWithAnyArgs().WriteAllTextAsync(default!, default!, default);
        Assert.Equal(StatusSeverity.Error, _sut.StatusSeverity);
    }

    [Fact]
    public async Task Copy_PutsPreviewOnClipboard()
    {
        await SignInAndSelectAsync(Web);
        Assert.False(_sut.CopyToClipboardCommand.CanExecute(null));
        await _sut.GenerateCommand.ExecuteAsync(null);

        _sut.CopyToClipboardCommand.Execute(null);

        _clipboard.Received(1).SetText("{\"generated\":true}");
    }

    [Fact]
    public async Task ChangingOptions_InvalidatesPreview()
    {
        await SignInAndSelectAsync(Web);
        await _sut.GenerateCommand.ExecuteAsync(null);
        Assert.NotEmpty(_sut.Preview);

        _sut.IsFunctionsFormat = !_sut.IsFunctionsFormat;

        Assert.Empty(_sut.Preview);
        Assert.False(_sut.CopyToClipboardCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(@"C:\p\local.settings.json", true)]
    [InlineData(@"C:\p\appsettings.Development.json", false)]
    public async Task Browse_PicksFormatFromFileName(string path, bool expectedFunctions)
    {
        _picker.PickSettingsFileAsync().Returns(path);
        _sut.IsFunctionsFormat = !expectedFunctions;

        await _sut.BrowseFileCommand.ExecuteAsync(null);

        Assert.Equal(path, _sut.SelectedFilePath);
        Assert.Equal(expectedFunctions, _sut.IsFunctionsFormat);
    }

    [Fact]
    public async Task Browse_Cancelled_KeepsPath()
    {
        _picker.PickSettingsFileAsync().Returns((string?)null);
        _sut.SelectedFilePath = "keep";

        await _sut.BrowseFileCommand.ExecuteAsync(null);

        Assert.Equal("keep", _sut.SelectedFilePath);
    }

    [Fact]
    public async Task SelectingAppService_EnablesGenerate_AndRaisesCanExecuteChanged()
    {
        await _sut.SignInCommand.ExecuteAsync(null);
        _sut.SelectedSubscription = Sub1;
        await WaitForAppServicesAsync();
        Assert.False(_sut.GenerateCommand.CanExecute(null));
        var raised = 0;
        _sut.GenerateCommand.CanExecuteChanged += (_, _) => raised++;

        _sut.SelectedAppService = Web;

        Assert.True(raised > 0);
        Assert.True(_sut.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public void FormatSelection_IsInverseOfFunctionsFormat()
    {
        Assert.True(_sut.IsAppSettingsFormat);
        _sut.IsFunctionsFormat = true;
        Assert.False(_sut.IsAppSettingsFormat);
        _sut.IsAppSettingsFormat = true;
        Assert.False(_sut.IsFunctionsFormat);
    }

    [Fact]
    public async Task SignIn_LoadsTenants_AndSelectsCurrentOne_WithoutSwitching()
    {
        await _sut.SignInCommand.ExecuteAsync(null);

        Assert.Equal([TenantA, TenantB], _sut.Tenants);
        Assert.Equal(TenantA, _sut.SelectedTenant);
        await _auth.DidNotReceiveWithAnyArgs().SwitchTenantAsync(default!, default);
    }

    [Fact]
    public async Task SignIn_TenantListFailure_IsNotFatal()
    {
        _azure.GetTenantsAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("no tenants"));

        await _sut.SignInCommand.ExecuteAsync(null);

        Assert.True(_sut.IsSignedIn);
        Assert.Empty(_sut.Tenants);
        Assert.Equal(2, _sut.Subscriptions.Count);
    }

    [Fact]
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
        Assert.Equal([tenantBSubs], _sut.Subscriptions);
        Assert.Equal(TenantB, _sut.SelectedTenant);
        Assert.Null(_sut.SelectedAppService);
    }

    [Fact]
    public async Task FailedTenantSwitch_ShowsError_AndRevertsSelection()
    {
        await _sut.SignInCommand.ExecuteAsync(null);
        _auth.SwitchTenantAsync(TenantB.Id, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("login cancelled"));

        _sut.SelectedTenant = TenantB;
        await WaitUntilAsync(() => !_sut.IsBusy && _sut.StatusSeverity == StatusSeverity.Error);

        Assert.Equal(TenantA, _sut.SelectedTenant);
        Assert.Contains("login cancelled", _sut.StatusMessage);
        Assert.Equal(2, _sut.Subscriptions.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    [Theory]
    [InlineData("jane.doe@contoso.com", "JA")]
    [InlineData("x@y.z", "X")]
    [InlineData("42@y.z", "?")]
    [InlineData(null, "?")]
    public void AccountInitials_AreDerivedFromUserName(string? account, string expected)
    {
        _sut.AccountName = account;

        Assert.Equal(expected, _sut.AccountInitials);
    }

    [Fact]
    public async Task PreviewTitle_FollowsFormatAndFile()
    {
        Assert.Equal("appsettings.json", _sut.PreviewTitle);

        _sut.IsFunctionsFormat = true;
        Assert.Equal("local.settings.json", _sut.PreviewTitle);

        _sut.IsFileTarget = true;
        _sut.SelectedFilePath = @"C:\p\appsettings.Development.json";
        Assert.Equal("appsettings.Development.json", _sut.PreviewTitle);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Generate_PublishesCountsAndPreviewFlags()
    {
        await SignInAndSelectAsync(Web);
        Assert.True(_sut.IsPreviewEmpty);

        await _sut.GenerateCommand.ExecuteAsync(null);

        Assert.True(_sut.HasPreviewText);
        Assert.False(_sut.IsPreviewEmpty);
        Assert.Equal((2, 1, 0), (_sut.AddedCount, _sut.OverriddenCount, _sut.UnchangedCount));

        _sut.IsFunctionsFormat = true; // invalidates the result

        Assert.True(_sut.IsPreviewEmpty);
        Assert.Equal((0, 0, 0), (_sut.AddedCount, _sut.OverriddenCount, _sut.UnchangedCount));
    }

    [Fact]
    public async Task Generate_WithWarnings_MentionsThemInStatus()
    {
        _resolver.ResolveAsync(Arg.Any<AzureAppConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(call => new KeyVaultResolutionResult(call.Arg<AzureAppConfiguration>(), 0, ["w1", "w2"]));
        await SignInAndSelectAsync(Web);

        await _sut.GenerateCommand.ExecuteAsync(null);

        Assert.Contains("2 warning(s)", _sut.StatusMessage);
    }

    [Fact]
    public void JsonTarget_IsInverseOfFileTarget()
    {
        Assert.True(_sut.IsJsonTarget);
        _sut.IsFileTarget = true;
        Assert.False(_sut.IsJsonTarget);
        _sut.IsJsonTarget = true;
        Assert.False(_sut.IsFileTarget);
    }
}
