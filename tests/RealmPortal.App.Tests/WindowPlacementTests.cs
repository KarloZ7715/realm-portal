using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

public class WindowPlacementFitTests
{
    private static readonly ScreenArea Primary = new(0, 0, 1920, 1040, 1.0);
    private static readonly ScreenArea Right = new(1920, 0, 2560, 1400, 1.0);

    [Fact]
    public void AVisiblePlacementIsKeptAsIs()
    {
        var saved = new WindowPlacement(100, 80, 1000, 600, Maximized: false);
        Assert.Equal(saved, WindowPlacementFit.Fit(saved, [Primary], 720, 440));
    }

    [Fact]
    public void MaximizedFlagIsKept()
    {
        var fit = WindowPlacementFit.Fit(new WindowPlacement(100, 80, 1000, 600, Maximized: true), [Primary], 720, 440);
        Assert.True(fit!.Maximized);
    }

    [Fact]
    public void SizeNeverGoesBelowTheMinimum()
    {
        var fit = WindowPlacementFit.Fit(new WindowPlacement(10, 10, 300, 200, false), [Primary], 720, 440)!;
        Assert.Equal(720, fit.Width);
        Assert.Equal(440, fit.Height);
    }

    [Fact]
    public void SizeIsCappedBelowTheWorkingAreaToLeaveRoomForTheFrame()
    {
        var fit = WindowPlacementFit.Fit(new WindowPlacement(0, 0, 4000, 3000, false), [Primary], 720, 440)!;
        Assert.Equal(1920 - WindowPlacementFit.DefaultFrameReserveWidthPx, fit.Width);
        Assert.Equal(1040 - WindowPlacementFit.DefaultFrameReserveHeightPx, fit.Height);
        Assert.Equal((0, 0), (fit.X, fit.Y));
        Assert.True(fit.Maximized);
    }

    [Fact]
    public void ASavedSizeThatFillsTheWorkingAreaIsRestoredMaximized()
    {
        var fit = WindowPlacementFit.Fit(new WindowPlacement(0, 0, 1920, 1040, Maximized: false), [Primary], 720, 440)!;
        Assert.True(fit.Maximized);
        Assert.True(fit.Width < 1920);
        Assert.True(fit.Height < 1040);
    }

    [Fact]
    public void APartlyOffScreenWindowIsPulledInside()
    {
        var fit = WindowPlacementFit.Fit(new WindowPlacement(1700, 900, 800, 500, false), [Primary], 720, 440)!;
        Assert.Equal(1920 - 800 - WindowPlacementFit.DefaultFrameReserveWidthPx, fit.X);
        Assert.Equal(1040 - 500 - WindowPlacementFit.DefaultFrameReserveHeightPx, fit.Y);
    }

    [Fact]
    public void AWindowOnAnUnpluggedMonitorIsCentredOnTheFirstScreen()
    {
        var saved = new WindowPlacement(2500, 300, 1000, 600, false);
        Assert.Equal(saved, WindowPlacementFit.Fit(saved, [Primary, Right], 720, 440));

        var fit = WindowPlacementFit.Fit(saved, [Primary], 720, 440)!;
        Assert.Equal((460, 220), (fit.X, fit.Y));
        Assert.Equal((1000.0, 600.0), (fit.Width, fit.Height));
    }

    [Fact]
    public void HiDpiScreensConvertTheSizeToPixels()
    {
        var hiDpi = new ScreenArea(0, 0, 2880, 1620, 2.0);
        var fit = WindowPlacementFit.Fit(new WindowPlacement(2000, 1000, 1000, 600, false), [hiDpi], 720, 440)!;
        Assert.Equal(2880 - 2000 - WindowPlacementFit.DefaultFrameReserveWidthPx, fit.X);
        Assert.Equal(1620 - 1200 - WindowPlacementFit.DefaultFrameReserveHeightPx, fit.Y);
        var capped = WindowPlacementFit.Fit(new WindowPlacement(0, 0, 3000, 3000, false), [hiDpi], 720, 440)!;
        Assert.Equal(
            ((2880 - WindowPlacementFit.DefaultFrameReserveWidthPx) / 2.0,
                (1620 - WindowPlacementFit.DefaultFrameReserveHeightPx) / 2.0),
            (capped.Width, capped.Height));
        Assert.True(capped.Maximized);
    }

    [Fact]
    public void OnAScreenSmallerThanTheMinimumTheMinimumWinsAtTheTopLeft()
    {
        var small = new ScreenArea(100, 50, 640, 400, 1.0);
        var fit = WindowPlacementFit.Fit(new WindowPlacement(300, 200, 1000, 600, false), [small], 720, 440)!;
        Assert.Equal((720.0, 440.0), (fit.Width, fit.Height));
        Assert.Equal((100, 50), (fit.X, fit.Y));
    }

    [Theory]
    [InlineData(double.NaN, 600)]
    [InlineData(0, 600)]
    [InlineData(1000, double.PositiveInfinity)]
    public void UnusableSizesFallBackToTheDefaults(double width, double height) =>
        Assert.Null(WindowPlacementFit.Fit(new WindowPlacement(0, 0, width, height, false), [Primary], 720, 440));

    [Fact]
    public void NoScreensFallBackToTheDefaults() =>
        Assert.Null(WindowPlacementFit.Fit(new WindowPlacement(0, 0, 1000, 600, false), [], 720, 440));

    [Fact]
    public void ATinyPositionOnlyShiftWhileMaximizingIsIgnored()
    {
        Assert.False(WindowPlacementFit.IsMeaningfulNormalUpdate(40, 30, 900, 560, 45, 35, 900, 560));
        Assert.False(WindowPlacementFit.IsMeaningfulNormalUpdate(40, 30, 900, 560, 40 + WindowPlacementFit.FrameExtentFudgePx, 30, 900, 560));
    }

    [Fact]
    public void ARealMoveOrResizeBeforeMaximizingIsKept()
    {
        Assert.True(WindowPlacementFit.IsMeaningfulNormalUpdate(40, 30, 900, 560, 120, 90, 900, 560));
        Assert.True(WindowPlacementFit.IsMeaningfulNormalUpdate(40, 30, 900, 560, 40, 30, 1000, 600));
        Assert.True(WindowPlacementFit.IsMeaningfulNormalUpdate(
            40, 30, 900, 560, 40 + WindowPlacementFit.FrameExtentFudgePx + 1, 30, 900, 560));
    }
}

public class WindowPlacementPersistenceTests
{
    [Fact]
    public void StoreKeepsThePlacementBesideTheOtherPreferences()
    {
        using var temp = new TempDirectory();
        var store = new UiPreferencesStore(temp.Path);
        store.Save(new UiPreferences("en", ["client-1"], FirstScanCompleted: true));

        store.SetWindowPlacement(new WindowPlacement(10, 20, 1000, 600, true));
        store.SetLanguage(RealmPortal.App.Localization.Language.Es);

        var loaded = store.Load();
        Assert.Equal(new WindowPlacement(10, 20, 1000, 600, true), loaded.Window);
        Assert.Equal("es", loaded.LanguageCode);
        Assert.Equal(["client-1"], loaded.ClientsInUse!);
    }

    [Fact]
    public void OlderPreferenceFilesWithoutAPlacementStillLoad()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "ui-preferences.json"), "{\"LanguageCode\":\"en\"}");
        Assert.Null(new UiPreferencesStore(temp.Path).Load().Window);
    }

    [Fact]
    public void ACorruptWindowEntryIsIgnoredWhileOtherPreferencesLoad()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "ui-preferences.json");
        File.WriteAllText(
            path,
            """
            {
              "LanguageCode": "en",
              "ClientsInUse": ["client-1"],
              "FirstScanCompleted": true,
              "Window": "not-an-object"
            }
            """);

        var loaded = new UiPreferencesStore(temp.Path).Load();
        Assert.Equal("en", loaded.LanguageCode);
        Assert.Equal(["client-1"], loaded.ClientsInUse!);
        Assert.True(loaded.FirstScanCompleted);
        Assert.Null(loaded.Window);
    }

    [Theory]
    [InlineData("""{"X":"bad","Y":1,"Width":1000,"Height":600,"Maximized":false}""")]
    [InlineData("""{"X":10,"Y":20,"Width":0,"Height":600,"Maximized":false}""")]
    [InlineData("""{"X":10}""")]
    [InlineData("""{"Width":1000,"Height":600}""")]
    [InlineData("""{"Y":20,"Width":1000,"Height":600,"Maximized":false}""")]
    [InlineData("42")]
    [InlineData("[1,2,3]")]
    public void MalformedWindowValuesAreTreatedAsNoPlacement(string windowJson)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "ui-preferences.json"),
            $$"""
            {
              "LanguageCode": "es",
              "ClientsInUse": ["keep-me"],
              "FirstScanCompleted": true,
              "Window": {{windowJson}}
            }
            """);

        var loaded = new UiPreferencesStore(temp.Path).Load();
        Assert.Equal("es", loaded.LanguageCode);
        Assert.Equal(["keep-me"], loaded.ClientsInUse!);
        Assert.True(loaded.FirstScanCompleted);
        Assert.Null(loaded.Window);
    }

    [AvaloniaFact]
    public void NoSavedPlacementKeepsTheDefaultWindow()
    {
        using var temp = new TempDirectory();
        var window = new MainWindow(MainViewModel.ForDemo(autoTick: false));
        WindowPlacementPersistence.Attach(window, new UiPreferencesStore(temp.Path));
        Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        Assert.Equal((720.0, 440.0), (window.Width, window.Height));
    }

    [AvaloniaFact]
    public void SavedPlacementIsRestoredAndSavedAgainOnClose()
    {
        using var temp = new TempDirectory();
        var store = new UiPreferencesStore(temp.Path);
        store.SetWindowPlacement(new WindowPlacement(40, 30, 1000, 600, Maximized: false));

        var window = new MainWindow(MainViewModel.ForDemo(autoTick: false));
        var persistence = WindowPlacementPersistence.Attach(window, store);
        Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
        Assert.Equal((1000.0, 600.0), (window.Width, window.Height));
        Assert.Equal(new PixelPoint(40, 30), window.Position);

        window.Show();
        Screenshots.Settle();
        Screenshots.Save(window, 1.0, "window-restored-1000x600-1.0x.png");
        window.Close();
        Assert.Equal(new WindowPlacement(40, 30, 1000, 600, false), store.Load().Window);
        Assert.False(persistence.Current.Maximized);
    }

    [AvaloniaFact]
    public void MaximizedIsRestoredButTheNormalSizeIsWhatGetsSaved()
    {
        using var temp = new TempDirectory();
        var store = new UiPreferencesStore(temp.Path);
        store.SetWindowPlacement(new WindowPlacement(40, 30, 900, 560, Maximized: true));

        var window = new MainWindow(MainViewModel.ForDemo(autoTick: false));
        var persistence = WindowPlacementPersistence.Attach(window, store);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.Equal(new PixelPoint(40, 30), new PixelPoint(persistence.Current.X, persistence.Current.Y));
        window.Show();
        Screenshots.Settle();
        window.Close();

        var saved = store.Load().Window!;
        Assert.True(saved.Maximized);
        Assert.Equal((900.0, 560.0), (saved.Width, saved.Height));
        Assert.Equal((40, 30), (saved.X, saved.Y));
    }

    [AvaloniaFact]
    public void AFrameSizedPositionNudgeWhileMaximizingDoesNotOffsetTheSavedRestoreBounds()
    {
        using var temp = new TempDirectory();
        var store = new UiPreferencesStore(temp.Path);
        store.SetWindowPlacement(new WindowPlacement(40, 30, 900, 560, Maximized: false));
        var window = new MainWindow(MainViewModel.ForDemo(autoTick: false));
        var persistence = WindowPlacementPersistence.Attach(window, store);
        window.Show();
        Screenshots.Settle();

        // Simulate the xfwm4 +5,+5 frame nudge that can arrive before WindowState flips to Maximized.
        window.Position = new PixelPoint(45, 35);
        window.WindowState = WindowState.Maximized;
        Assert.Equal((40, 30), (persistence.Current.X, persistence.Current.Y));
        window.Close();

        var saved = store.Load().Window!;
        Assert.True(saved.Maximized);
        Assert.Equal((40, 30), (saved.X, saved.Y));
        Assert.Equal((900.0, 560.0), (saved.Width, saved.Height));
    }

    [AvaloniaFact]
    public void AMoveRightBeforeMaximizingIsNotLost()
    {
        using var temp = new TempDirectory();
        var store = new UiPreferencesStore(temp.Path);
        store.SetWindowPlacement(new WindowPlacement(40, 30, 900, 560, Maximized: false));
        var window = new MainWindow(MainViewModel.ForDemo(autoTick: false));
        WindowPlacementPersistence.Attach(window, store);
        window.Show();
        Screenshots.Settle();

        // Move, then maximize before the deferred capture has run.
        window.Position = new PixelPoint(120, 90);
        window.WindowState = WindowState.Maximized;
        window.Close();

        var saved = store.Load().Window!;
        Assert.True(saved.Maximized);
        Assert.Equal((120, 90), (saved.X, saved.Y));
        Assert.Equal((900.0, 560.0), (saved.Width, saved.Height));
    }

    [AvaloniaFact]
    public void MinimizedIsNeverSavedAsMinimized()
    {
        using var temp = new TempDirectory();
        var store = new UiPreferencesStore(temp.Path);
        var window = new MainWindow(MainViewModel.ForDemo(autoTick: false));
        var persistence = WindowPlacementPersistence.Attach(window, store);
        window.Show();
        window.WindowState = WindowState.Maximized;
        window.WindowState = WindowState.Minimized;
        Assert.True(persistence.Current.Maximized);
        window.WindowState = WindowState.Normal;
        window.WindowState = WindowState.Minimized;
        Assert.False(persistence.Current.Maximized);
        window.Close();
        Assert.NotNull(store.Load().Window);
    }
}
