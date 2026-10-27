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
    /// Horizontal pixels reserved for the window-manager frame (left+right borders) when capping the client size.
    /// Without this, a saved size equal to the working area restores slightly larger than the screen once the frame is added.
    /// </summary>
    public const int DefaultFrameReserveWidthPx = 16;

    /// <summary>
    /// Vertical pixels reserved for the window-manager frame (title bar + bottom border) when capping the client size.
    /// </summary>
    public const int DefaultFrameReserveHeightPx = 40;

    /// <summary>
    /// Position-only shifts up to this many pixels are treated as window-manager frame offsets while maximizing,
    /// not as a real move of the restore bounds (xfwm4 reports about +5,+5).
    /// </summary>
    public const int FrameExtentFudgePx = 16;

    /// <summary>
    /// Returns the placement to restore: on the screen that holds most of the saved window (or the first screen when
    /// none does, e.g. the monitor was unplugged), no smaller than <paramref name="minWidth"/>×<paramref name="minHeight"/>,
    /// no larger than that screen's working area minus a frame reserve, and moved so it lies fully inside it. The minimum wins over the
    /// working area: on a screen smaller than the minimum the window keeps the minimum size at the screen's top-left corner. Null means "use the defaults"
    /// (no screens, or a saved size that is not a finite positive number).
    /// </summary>
    public static WindowPlacement? Fit(
        WindowPlacement saved,
        IReadOnlyList<ScreenArea> screens,
        double minWidth,
        double minHeight,
        int frameReserveWidthPx = DefaultFrameReserveWidthPx,
        int frameReserveHeightPx = DefaultFrameReserveHeightPx)
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

        var usableWidthPx = Math.Max(0, screen.Width - Math.Max(0, frameReserveWidthPx));
        var usableHeightPx = Math.Max(0, screen.Height - Math.Max(0, frameReserveHeightPx));
        var maxWidth = Math.Max(minWidth, usableWidthPx / scaling);
        var maxHeight = Math.Max(minHeight, usableHeightPx / scaling);
        var width = Math.Clamp(saved.Width, minWidth, maxWidth);
        var height = Math.Clamp(saved.Height, minHeight, maxHeight);
        var widthPx = (int)Math.Ceiling(width * scaling);
        var heightPx = (int)Math.Ceiling(height * scaling);

        var frameW = Math.Max(0, frameReserveWidthPx);
        var frameH = Math.Max(0, frameReserveHeightPx);
        int x, y;
        if (visible)
        {
            // Leave frame clearance at the right/bottom so chrome outside the client stays on-screen.
            x = Math.Clamp(saved.X, screen.X, Math.Max(screen.X, screen.Right - widthPx - frameW));
            y = Math.Clamp(saved.Y, screen.Y, Math.Max(screen.Y, screen.Bottom - heightPx - frameH));
        }
        else
        {
            x = screen.X + Math.Max(0, (screen.Width - widthPx) / 2);
            y = screen.Y + Math.Max(0, (screen.Height - heightPx) / 2);
        }

        // A saved size that already fills the working area (before the frame reserve) is restored maximized so the
        // framed window does not overflow the screen on the next run.
        var fillsWorkingArea =
            saved.Width * scaling >= screen.Width - 2 &&
            saved.Height * scaling >= screen.Height - 2;
        var maximized = saved.Maximized || fillsWorkingArea;

        return new WindowPlacement(x, y, width, height, maximized);
    }

    /// <summary>
    /// Whether a pending Normal-state capture should replace the remembered restore bounds when leaving Normal
    /// (e.g. maximizing). Ignores tiny position-only shifts from the window-manager frame.
    /// </summary>
    public static bool IsMeaningfulNormalUpdate(
        int normalX,
        int normalY,
        double normalWidth,
        double normalHeight,
        int pendingX,
        int pendingY,
        double pendingWidth,
        double pendingHeight,
        int frameExtentFudgePx = FrameExtentFudgePx)
    {
        var sizeChanged =
            Math.Abs(pendingWidth - normalWidth) > 1 ||
            Math.Abs(pendingHeight - normalHeight) > 1;
        if (sizeChanged)
        {
            return true;
        }

        return Math.Abs(pendingX - normalX) > frameExtentFudgePx
               || Math.Abs(pendingY - normalY) > frameExtentFudgePx;
    }

    /// <summary>True when width/height are finite and positive (safe to restore).</summary>
    public static bool HasUsableSize(WindowPlacement placement) =>
        placement is not null && IsUsable(placement.Width) && IsUsable(placement.Height);

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
