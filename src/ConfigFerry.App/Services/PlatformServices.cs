using ConfigFerry.Core.Abstractions;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ConfigFerry.App.Services;

/// <summary>Gives services access to the main window handle (needed to parent pickers in unpackaged apps).</summary>
public sealed class WindowProvider : IWindowHandleProvider
{
    public Window? Window { get; set; }

    public nint Handle =>
        WindowNative.GetWindowHandle(Window ?? throw new InvalidOperationException("The main window is not created yet."));
}

public sealed class FilePickerService(IWindowHandleProvider windowProvider) : IFilePickerService
{
    public async Task<string?> PickSettingsFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, windowProvider.Handle);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
