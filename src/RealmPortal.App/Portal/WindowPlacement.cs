namespace RealmPortal.App.Portal;

/// <summary>
/// The main window's last normal (not maximized, not minimized) bounds and whether it was maximized.
/// <see cref="X"/>/<see cref="Y"/> are screen pixels (Avalonia <c>Window.Position</c>); <see cref="Width"/>/<see cref="Height"/>
/// are device-independent units (Avalonia <c>Window.Width</c>/<c>Height</c>).
/// </summary>
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized);

/// <summary>A screen's working area in pixels and its scale factor (pixels per device-independent unit).</summary>
public readonly record struct ScreenArea(int X, int Y, int Width, int Height, double Scaling)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>Fits a saved <see cref="WindowPlacement"/> onto the screens that exist now.</summary>
public static class WindowPlacementFit
{
    /// <summary>
    /// Returns the placement to restore: on the screen that holds most of the saved window (or the first screen when
    /// none does, e.g. the monitor was unplugged), no smaller than <paramref name="minWidth"/>×<paramref name="minHeight"/>,
    /// no larger than that screen's working area, and moved so it lies fully inside it. The minimum wins over the
    /// working area: on a screen smaller than the minimum the window keeps the minimum size at the screen's top-left corner. Null means "use the defaults"
    /// (no screens, or a saved size that is not a finite positive number).
    /// </summary>
    public static WindowPlacement? Fit(WindowPlacement saved, IReadOnlyList<ScreenArea> screens, double minWidth, double minHeight)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (screens.Count == 0 || !IsUsable(saved.Width) || !IsUsable(saved.Height))
        {
            return null;
        }

        var savedOn = screens.MaxBy(s => Overlap(saved, s));
        var visible = Overlap(saved, savedOn) > 0;
        var screen = visible ? savedOn : screens[0];
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;

        var maxWidth = Math.Max(minWidth, screen.Width / scaling);
        var maxHeight = Math.Max(minHeight, screen.Height / scaling);
        var width = Math.Clamp(saved.Width, minWidth, maxWidth);
        var height = Math.Clamp(saved.Height, minHeight, maxHeight);
        var widthPx = (int)Math.Ceiling(width * scaling);
        var heightPx = (int)Math.Ceiling(height * scaling);

        int x, y;
        if (visible)
        {
            x = Math.Clamp(saved.X, screen.X, Math.Max(screen.X, screen.Right - widthPx));
            y = Math.Clamp(saved.Y, screen.Y, Math.Max(screen.Y, screen.Bottom - heightPx));
        }
        else
        {
            x = screen.X + Math.Max(0, (screen.Width - widthPx) / 2);
            y = screen.Y + Math.Max(0, (screen.Height - heightPx) / 2);
        }

        return new WindowPlacement(x, y, width, height, saved.Maximized);
    }

    private static bool IsUsable(double value) => double.IsFinite(value) && value > 0;

    /// <summary>Overlapping area in pixels between the saved window and a screen (the size is converted with that screen's scale).</summary>
    private static long Overlap(WindowPlacement saved, ScreenArea screen)
    {
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var right = saved.X + (long)Math.Ceiling(saved.Width * scaling);
        var bottom = saved.Y + (long)Math.Ceiling(saved.Height * scaling);
        var w = Math.Min(right, screen.Right) - Math.Max(saved.X, screen.X);
        var h = Math.Min(bottom, screen.Bottom) - Math.Max(saved.Y, screen.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }
}
