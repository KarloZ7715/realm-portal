using Avalonia;
using RealmPortal.Core;

namespace RealmPortal.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ExceptionLog.InstallProcessHandlers();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
