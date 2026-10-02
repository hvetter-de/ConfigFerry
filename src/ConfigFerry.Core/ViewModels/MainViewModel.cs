using System.Collections.ObjectModel;
using Azure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Azure;
using ConfigFerry.Core.Models;
using Microsoft.Extensions.Logging;

namespace ConfigFerry.Core.ViewModels;

public enum StatusSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}

public sealed partial class MainViewModel(
    IAzureAuthService auth,
    IAzureResourceService azure,
    IKeyVaultReferenceResolver keyVaultResolver,
    IConfigGenerator generator,
    IFilePickerService filePicker,
    IClipboardService clipboard,
    ITextFileStore files,
    ILogger<MainViewModel> logger) : ObservableObject, IDisposable
{
    private const string FunctionsFileName = "local.settings.json";

    private CancellationTokenSource? _appServicesCts;

    // File content the preview was generated from; used to detect concurrent edits before saving.
    private string? _baselineFileContent;

    // Set while the VM itself changes SelectedTenant, so that does not trigger a tenant switch.
    private bool _suppressTenantSwitch;

    public ObservableCollection<TenantInfo> Tenants { get; } = [];

    public ObservableCollection<SubscriptionInfo> Subscriptions { get; } = [];

    public ObservableCollection<AppServiceInfo> AppServices { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    // ---- Authentication ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut), nameof(CanChangeSource))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(SignOutCommand), nameof(GenerateCommand))]
    public partial bool IsSignedIn { get; set; }

    /// <summary>Tenant / subscription / app pickers are usable only when signed in and idle.</summary>
    public bool CanChangeSource => IsSignedIn && !IsBusy;

    public bool IsSignedOut => !IsSignedIn;

    [ObservableProperty]
    public partial string? AccountName { get; set; }

    // ---- Source ----

    [ObservableProperty]
    public partial TenantInfo? SelectedTenant { get; set; }

    [ObservableProperty]
    public partial SubscriptionInfo? SelectedSubscription { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    public partial AppServiceInfo? SelectedAppService { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingAppServices { get; set; }

    // ---- Target ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJsonTarget))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(SaveToFileCommand))]
    public partial bool IsFileTarget { get; set; }

    public bool IsJsonTarget
    {
        get => !IsFileTarget;
        set => IsFileTarget = !value;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    public partial string? SelectedFilePath { get; set; }

    /// <summary>False: appsettings.json (ASP.NET). True: local.settings.json (Azure Functions).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppSettingsFormat))]
    public partial bool IsFunctionsFormat { get; set; }

    public bool IsAppSettingsFormat
    {
        get => !IsFunctionsFormat;
        set => IsFunctionsFormat = !value;
    }

    // ---- Options ----

    [ObservableProperty]
    public partial bool ResolveKeyVaultReferences { get; set; } = true;

    [ObservableProperty]
    public partial bool ExcludePlatformSettings { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeConnectionStrings { get; set; } = true;

    [ObservableProperty]
    public partial bool CreateBackup { get; set; } = true;

    // ---- Result ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand), nameof(SaveToFileCommand))]
    public partial string Preview { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Summary { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial StatusSeverity StatusSeverity { get; set; } = StatusSeverity.Informational;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeSource))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(SaveToFileCommand), nameof(SignInCommand))]
    public partial bool IsBusy { get; set; }

    public bool HasWarnings => Warnings.Count > 0;

    public void Dispose()
    {
        _appServicesCts?.Cancel();
        _appServicesCts?.Dispose();
    }

    // ---- Lifecycle ----

    /// <summary>Restores the previous session silently and loads subscriptions if that succeeded.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await auth.TrySignInSilentlyAsync(cancellationToken))
            {
                await OnSignedInAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure(ex, "restore session");
        }
    }

    // ---- Commands ----

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        await RunBusyAsync("Signing in", async () =>
        {
            await auth.SignInAsync(cancellationToken);
            await OnSignedInAsync(cancellationToken);
        });
    }

    private bool CanSignIn() => !IsSignedIn && !IsBusy;

    [RelayCommand(CanExecute = nameof(IsSignedIn))]
    private void SignOut()
    {
        auth.SignOut();
        _appServicesCts?.Cancel();
        IsSignedIn = false;
        AccountName = null;
        Tenants.Clear();
        Subscriptions.Clear();
        AppServices.Clear();
        SelectTenant(null);
        SelectedSubscription = null;
        SelectedAppService = null;
        ClearResult();
        SetStatus(StatusSeverity.Informational, "Signed out.");
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!IsSignedIn)
        {
            return;
        }

        await RunBusyAsync("Refreshing subscriptions", () => LoadSubscriptionsAsync(cancellationToken));
    }

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        var path = await filePicker.PickSettingsFileAsync();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        SelectedFilePath = path;

        // The file name is a strong hint for the format.
        var name = Path.GetFileName(path);
        if (name.Equals(FunctionsFileName, StringComparison.OrdinalIgnoreCase))
        {
            IsFunctionsFormat = true;
        }
        else if (name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
        {
            IsFunctionsFormat = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        await RunBusyAsync("Generating configuration", async () =>
        {
            ClearResult();

            string? existing = null;
            if (IsFileTarget)
            {
                var path = SelectedFilePath!;
                if (!files.Exists(path))
                {
                    throw new ConfigFerryException($"The file '{path}' does not exist.");
                }

                existing = await files.ReadAllTextAsync(path, cancellationToken);
            }

            SetStatus(StatusSeverity.Informational, "Reading configuration from Azure…");
            var configuration = await azure.GetConfigurationAsync(SelectedAppService!, cancellationToken);

            var warnings = new List<string>();
            var keyVaultCount = ResolveKeyVaultReferences ? 0 : CountReferences(configuration);
            if (ResolveKeyVaultReferences)
            {
                SetStatus(StatusSeverity.Informational, "Resolving Key Vault references…");
                var resolution = await keyVaultResolver.ResolveAsync(configuration, cancellationToken);
                configuration = resolution.Configuration;
                warnings.AddRange(resolution.Warnings);
            }
            else if (keyVaultCount > 0)
            {
                warnings.Add($"{keyVaultCount} setting(s) are Key Vault references and were copied unresolved.");
            }

            var options = new GenerationOptions(
                IsFunctionsFormat ? ConfigFormat.FunctionLocalSettings : ConfigFormat.AppSettings,
                ExcludePlatformSettings,
                IncludeConnectionStrings);
            var result = generator.Generate(configuration, options, existing);
            warnings.AddRange(result.Warnings);

            _baselineFileContent = existing;
            Preview = result.Json;
            foreach (var warning in warnings)
            {
                Warnings.Add(warning);
            }

            OnPropertyChanged(nameof(HasWarnings));
            Summary = $"{result.Stats.Added} added · {result.Stats.Overridden} overridden by Azure · {result.Stats.Unchanged} unchanged";
            SetStatus(
                warnings.Count > 0 ? StatusSeverity.Warning : StatusSeverity.Success,
                IsFileTarget ? "Merged preview ready. Review it, then save to the file." : "Configuration generated.");
        });
    }

    private bool CanGenerate() =>
        IsSignedIn && !IsBusy && SelectedAppService is not null
        && (!IsFileTarget || !string.IsNullOrWhiteSpace(SelectedFilePath));

    [RelayCommand(CanExecute = nameof(HasPreview))]
    private void CopyToClipboard()
    {
        clipboard.SetText(Preview);
        SetStatus(StatusSeverity.Success, "Copied to clipboard.");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveToFileAsync(CancellationToken cancellationToken)
    {
        await RunBusyAsync("Saving file", async () =>
        {
            var path = SelectedFilePath!;
            var current = files.Exists(path) ? await files.ReadAllTextAsync(path, cancellationToken) : null;
            if (!string.Equals(current, _baselineFileContent, StringComparison.Ordinal))
            {
                throw new ConfigFerryException("The file changed on disk after the preview was generated. Generate again to avoid losing changes.");
            }

            if (CreateBackup && current is not null)
            {
                files.Copy(path, path + ".bak", overwrite: true);
            }

            await files.WriteAllTextAsync(path, Preview, cancellationToken);
            _baselineFileContent = Preview;
            SetStatus(StatusSeverity.Success, $"Saved to {path}.");
        });
    }

    private bool CanSave() => IsFileTarget && HasPreview() && !IsBusy;

    private bool HasPreview() => !string.IsNullOrEmpty(Preview);

    // ---- Change handlers ----

    partial void OnSelectedSubscriptionChanged(SubscriptionInfo? value) => _ = LoadAppServicesAsync(value);

    partial void OnSelectedAppServiceChanged(AppServiceInfo? value)
    {
        ClearResult();
        if (value is not null)
        {
            // Suggest the matching format; the user can still override it.
            IsFunctionsFormat = value.Kind == AppServiceKind.FunctionApp;
        }
    }

    partial void OnIsFunctionsFormatChanged(bool value) => ClearResult();

    partial void OnIsFileTargetChanged(bool value) => ClearResult();

    partial void OnSelectedFilePathChanged(string? value) => ClearResult();

    partial void OnResolveKeyVaultReferencesChanged(bool value) => ClearResult();

    partial void OnExcludePlatformSettingsChanged(bool value) => ClearResult();

    partial void OnIncludeConnectionStringsChanged(bool value) => ClearResult();

    // ---- Helpers ----

    private async Task OnSignedInAsync(CancellationToken cancellationToken)
    {
        IsSignedIn = true;
        AccountName = auth.AccountName;
        await LoadTenantsAsync(cancellationToken);
        await LoadSubscriptionsAsync(cancellationToken);
    }

    private async Task LoadTenantsAsync(CancellationToken cancellationToken)
    {
        Tenants.Clear();
        try
        {
            foreach (var tenant in await azure.GetTenantsAsync(cancellationToken))
            {
                Tenants.Add(tenant);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not fatal: the current tenant's subscriptions can still be used.
            LogFailure(ex, "load tenants");
        }

        SelectTenant(auth.TenantId);
    }

    partial void OnSelectedTenantChanged(TenantInfo? value)
    {
        if (_suppressTenantSwitch || value is null
            || string.Equals(value.Id, auth.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = SwitchTenantAsync(value);
    }

    private async Task SwitchTenantAsync(TenantInfo tenant)
    {
        await RunBusyAsync("Switching tenant", async () =>
        {
            SetStatus(StatusSeverity.Informational, $"Signing in to {tenant.DisplayName}… complete the prompt in your browser if asked.");
            await auth.SwitchTenantAsync(tenant.Id, CancellationToken.None);

            Subscriptions.Clear();
            SelectedSubscription = null;
            ClearResult();
            await LoadSubscriptionsAsync(CancellationToken.None);
        });

        // On failure (or cancel) show the tenant that is actually active.
        SelectTenant(auth.TenantId);
    }

    private void SelectTenant(string? tenantId)
    {
        _suppressTenantSwitch = true;
        try
        {
            SelectedTenant = Tenants.FirstOrDefault(t => string.Equals(t.Id, tenantId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressTenantSwitch = false;
        }
    }

    private async Task LoadSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var subscriptions = await azure.GetSubscriptionsAsync(cancellationToken);
        Subscriptions.Clear();
        foreach (var subscription in subscriptions)
        {
            Subscriptions.Add(subscription);
        }

        SelectedSubscription = subscriptions.Count == 1 ? subscriptions[0] : null;
        SetStatus(
            subscriptions.Count == 0 ? StatusSeverity.Warning : StatusSeverity.Informational,
            subscriptions.Count == 0
                ? "No subscriptions are visible to this account."
                : $"Signed in. {subscriptions.Count} subscription(s) found.");
    }

    private async Task LoadAppServicesAsync(SubscriptionInfo? subscription)
    {
        // A newer selection supersedes any request still in flight.
        var previous = _appServicesCts;
        if (previous is not null)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }

        var cts = _appServicesCts = new CancellationTokenSource();

        AppServices.Clear();
        SelectedAppService = null;
        if (subscription is null)
        {
            IsLoadingAppServices = false;
            return;
        }

        IsLoadingAppServices = true;
        try
        {
            var apps = await azure.GetAppServicesAsync(subscription.Id, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            foreach (var app in apps)
            {
                AppServices.Add(app);
            }

            if (apps.Count == 0)
            {
                SetStatus(StatusSeverity.Informational, "No web apps or function apps in this subscription.");
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded or cancelled.
        }
        catch (Exception ex)
        {
            ReportFailure("Loading app services", ex);
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                IsLoadingAppServices = false;
            }
        }
    }

    private async Task RunBusyAsync(string operation, Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            SetStatus(StatusSeverity.Informational, $"{operation} was cancelled.");
        }
        catch (Exception ex)
        {
            ReportFailure(operation, ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReportFailure(string operation, Exception ex)
    {
        LogFailure(ex, operation);
        var message = ex switch
        {
            ConfigFerryException => ex.Message,
            RequestFailedException { Status: 401 or 403 } =>
                "Access denied. Make sure your account can read this app's configuration (e.g. Website Contributor).",
            RequestFailedException rf => $"Azure returned an error: {rf.Message}",
            _ => ex.Message,
        };
        SetStatus(StatusSeverity.Error, $"{operation} failed: {message}");
    }

    private static int CountReferences(AzureAppConfiguration configuration) =>
        configuration.AppSettings.Values.Concat(configuration.ConnectionStrings.Values).Count(KeyVaultReference.IsReference);

    private void ClearResult()
    {
        Preview = string.Empty;
        Summary = null;
        _baselineFileContent = null;
        Warnings.Clear();
        OnPropertyChanged(nameof(HasWarnings));
    }

    private void SetStatus(StatusSeverity severity, string message)
    {
        StatusSeverity = severity;
        StatusMessage = message;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Operation failed: {Operation}")]
    private partial void LogFailure(Exception exception, string operation);
}

