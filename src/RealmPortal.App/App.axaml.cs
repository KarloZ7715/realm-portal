using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = desktop.Args?.Contains("--gallery") == true
                ? new Dev.GalleryWindow()
                : desktop.Args?.Contains("--demo") == true
                    ? new MainWindow(MainViewModel.ForDemo())
                    : new MainWindow(new MainViewModel(PortalSession.Create(), PickClientFolderAsync));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task<string?> PickClientFolderAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return null;
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localization.Loc.Get("clients.pick"),
            AllowMultiple = false,
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
