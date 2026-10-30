using Avalonia;
using RealmPortal.App.Platform;

namespace RealmPortal.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // One window per config directory (#197): a second launch focuses the running one and exits.
        using var instance = AppInstance.Claim(args);
        if (instance.ShouldExit)
            return 0;
        // SIGTERM (logout, `kill`) does not unwind Main: remove the socket there too. A leftover is harmless anyway.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => instance.Dispose();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
