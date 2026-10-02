using ConfigFerry.App.Services;
using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Azure;
using ConfigFerry.Core.Configuration;
using ConfigFerry.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace ConfigFerry.App;

public partial class App : Application
{
    private readonly IHost _host;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

        var builder = Host.CreateApplicationBuilder();
        ConfigureServices(builder.Services);
        _host = builder.Build();
    }

    public static T GetService<T>()
        where T : notnull =>
        ((App)Current)._host.Services.GetRequiredService<T>();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        GetService<WindowProvider>().Window = _window;
        _window.Activate();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core
        services.AddSingleton<IAzureAuthService, AzureAuthService>();
        services.AddSingleton<IAzureResourceService, AzureResourceService>();
        services.AddSingleton<ISecretReader, KeyVaultSecretReader>();
        services.AddSingleton<IKeyVaultReferenceResolver, KeyVaultReferenceResolver>();
        services.AddSingleton<IConfigGenerator, ConfigGenerator>();
        services.AddSingleton<ITextFileStore, TextFileStore>();

        // Platform (WinUI)
        services.AddSingleton<WindowProvider>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IClipboardService, ClipboardService>();

        services.AddSingleton<MainViewModel>();
    }
}
