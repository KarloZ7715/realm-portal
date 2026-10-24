using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace RealmPortal.App.Portal;

/// <summary>
/// Restores the main window's size, position and maximized state from <see cref="UiPreferencesStore"/> and saves them on close.
/// Only the last normal bounds are kept (a maximized window remembers the size it returns to), and a minimized window is
/// never restored minimized. This is plain Avalonia: on X11 the reported position can differ from the one that was set by
/// the size of the window-manager frame (see docs/ui-screens.md).
/// </summary>
public sealed class WindowPlacementPersistence
{
    private readonly Window _window;
    private readonly UiPreferencesStore _store;
    private PixelPoint _normalPosition;
    private Size _normalSize;
    private bool _maximizedBeforeMinimize;

    private WindowPlacementPersistence(Window window, UiPreferencesStore store)
    {
        _window = window;
        _store = store;
        _normalPosition = window.Position;
        _normalSize = new Size(window.Width, window.Height);
    }

    /// <summary>Applies the saved placement (call before the window is shown) and saves the placement when it closes.</summary>
    public static WindowPlacementPersistence Attach(Window window, UiPreferencesStore store)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(store);
        var persistence = new WindowPlacementPersistence(window, store);
        persistence.Restore(store.Load().Window);
        window.Opened += (_, _) => persistence.CaptureNormal();
        window.PositionChanged += (_, _) => persistence.CaptureNormalLater();
        window.Resized += (_, _) => persistence.CaptureNormalLater();
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                persistence.OnWindowStateChanged((WindowState)e.OldValue!, (WindowState)e.NewValue!);
            }
        };
        window.Closing += (_, _) => persistence.Save();
        return persistence;
    }

    /// <summary>What would be saved now.</summary>
    public WindowPlacement Current
    {
        get
        {
            var state = _window.WindowState;
            var maximized = state is WindowState.Maximized or WindowState.FullScreen
                            || (state == WindowState.Minimized && _maximizedBeforeMinimize);
            return new WindowPlacement(_normalPosition.X, _normalPosition.Y, _normalSize.Width, _normalSize.Height, maximized);
        }
    }

    private void Restore(WindowPlacement? saved)
    {
        if (saved is null)
        {
            return;
        }

        var screens = _window.Screens.All
            .Select(s => new ScreenArea(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height, s.Scaling))
            .ToList();
        var fit = WindowPlacementFit.Fit(saved, screens, _window.MinWidth, _window.MinHeight);
        if (fit is null)
        {
            return;
        }

        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Width = fit.Width;
        _window.Height = fit.Height;
        _window.Position = new PixelPoint(fit.X, fit.Y);
        _normalPosition = _window.Position;
        _normalSize = new Size(fit.Width, fit.Height);
        if (fit.Maximized)
        {
            _window.WindowState = WindowState.Maximized;
        }
    }

    private void OnWindowStateChanged(WindowState oldState, WindowState newState)
    {
        if (newState == WindowState.Minimized)
        {
            _maximizedBeforeMinimize = oldState is WindowState.Maximized or WindowState.FullScreen;
        }
    }

    // Size and state change notifications can arrive in either order when maximizing, so the capture waits for both.
    private void CaptureNormalLater() => Dispatcher.UIThread.Post(CaptureNormal, DispatcherPriority.Background);

    private void CaptureNormal()
    {
        if (_window.WindowState != WindowState.Normal)
        {
            return;
        }

        _normalPosition = _window.Position;
        var size = _window.ClientSize;
        if (size.Width > 0 && size.Height > 0)
        {
            _normalSize = size;
        }
    }

    private void Save()
    {
        CaptureNormal();
        try
        {
            _store.SetWindowPlacement(Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Closing must never fail because the preferences file cannot be written.
        }
    }
}
