using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RealmPortal.App.Controls;

namespace RealmPortal.App;

public partial class App : Application
{
    public override void Initialize()
    {
        _ = typeof(ButtonSurface);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // "--gallery" opens the development-only component gallery instead of the app.
            desktop.MainWindow = desktop.Args?.Contains("--gallery") == true ? new Dev.GalleryWindow() : new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
