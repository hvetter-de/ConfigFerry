using ConfigFerry.App.Services;
using ConfigFerry.Core;
using ConfigFerry.Core.Abstractions;
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
        // Everything UI-independent (Azure access, merge logic, view model), always consumed via interfaces
        services.AddConfigFerryCore();

        // Platform services that need WinUI
        services.AddSingleton<WindowProvider>();
        services.AddSingleton<IWindowHandleProvider>(sp => sp.GetRequiredService<WindowProvider>());
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
    }
}
