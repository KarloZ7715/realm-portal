using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace RealmPortal.App.Tests;

/// <summary>Renders windows headlessly at a chosen render scale and saves PNGs for visual review.</summary>
internal static class Screenshots
{
    /// <summary>Output folder: $RP_SCREENSHOT_DIR when set, otherwise a "screenshots" folder next to the test binaries.</summary>
    public static string OutputDirectory
    {
        get
        {
            var directory = Environment.GetEnvironmentVariable("RP_SCREENSHOT_DIR")
                            ?? Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    /// <summary>
    /// Sets the headless window's render scale (the headless backend has no public setter, so the scale is written
    /// through reflection and announced the way a real DPI change is), lets layout and rendering settle, and captures.
    /// </summary>
    public static Avalonia.Media.Imaging.WriteableBitmap Capture(Window window, double scale)
    {
        window.Show();
        SetRenderScaling(window, scale);
        Settle();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame was rendered");
        return frame;
    }

    public static void Save(Window window, double scale, string name)
    {
        using var frame = Capture(window, scale);
        frame.Save(Path.Combine(OutputDirectory, name));
    }

    public static void SetRenderScaling(Window window, double scale)
    {
        var impl = window.PlatformImpl ?? throw new InvalidOperationException("window is not shown");
        var type = impl.GetType();
        type.GetField("<RenderScaling>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(impl, scale);
        var changed = (Action<double>?)type.GetProperty("ScalingChanged")!.GetValue(impl);
        changed?.Invoke(scale);
    }

    public static void Settle()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
