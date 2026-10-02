using ConfigFerry.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConfigFerry.App.Views;

public sealed partial class MainView : UserControl
{
    public MainView()
    {
        ViewModel = App.GetService<MainViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public MainViewModel ViewModel { get; }

    // x:Bind helper functions
    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);

    public static InfoBarSeverity MapSeverity(StatusSeverity severity) => severity switch
    {
        StatusSeverity.Success => InfoBarSeverity.Success,
        StatusSeverity.Warning => InfoBarSeverity.Warning,
        StatusSeverity.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }
}
