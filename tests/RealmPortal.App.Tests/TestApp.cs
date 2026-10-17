using Avalonia;
using Avalonia.Headless;
using RealmPortal.App;
using RealmPortal.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace RealmPortal.App.Tests;

/// <summary>Headless Avalonia host with real Skia rendering, so screenshots show what the app draws.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RealmPortal.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
