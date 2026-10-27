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
    private (PixelPoint Position, Size Size)? _pending;
    private (PixelPoint Position, Size Size)? _normalBeforePending;
    private bool _maximizedBeforeMinimize;
    private bool _restoredPlacement;

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
        window.Opened += (_, _) => persistence.CaptureInitialNormal();
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
        _restoredPlacement = true;
        // Keep the fitted restore bounds, not whatever the WM reports after SetPosition (xfwm4 can shift by +5,+5).
        _normalPosition = new PixelPoint(fit.X, fit.Y);
        _normalSize = new Size(fit.Width, fit.Height);
        if (fit.Maximized)
        {
            _window.WindowState = WindowState.Maximized;
        }
    }

    private void OnWindowStateChanged(WindowState oldState, WindowState newState)
    {
        if (oldState == WindowState.Normal
            && newState is (WindowState.Maximized or WindowState.FullScreen))
        {
            var normalPosition = _normalBeforePending?.Position ?? _normalPosition;
            var normalSize = _normalBeforePending?.Size ?? _normalSize;
            // Keep a move or resize that happened just before maximizing, unless what was seen is already the
            // maximized size (the size notification can arrive before the state change). Ignore tiny
            // position-only frame nudges from the window manager.
            if (_pending is { } pending
                && !LooksMaximized(pending.Size)
                && WindowPlacementFit.IsMeaningfulNormalUpdate(
                    normalPosition.X,
                    normalPosition.Y,
                    normalSize.Width,
                    normalSize.Height,
                    pending.Position.X,
                    pending.Position.Y,
                    pending.Size.Width,
                    pending.Size.Height))
            {
                (_normalPosition, _normalSize) = pending;
            }
            else if (_normalBeforePending is { } before)
            {
                (_normalPosition, _normalSize) = before;
            }

            _pending = null;
            _normalBeforePending = null;
        }
        else if (oldState == WindowState.Normal && newState == WindowState.Minimized)
        {
            // A move/resize just before minimize must still be saved; do not apply the maximize filter.
            if (_pending is { } pending)
            {
                (_normalPosition, _normalSize) = pending;
            }

            _pending = null;
            _normalBeforePending = null;
        }

        if (newState == WindowState.Minimized)
        {
            _maximizedBeforeMinimize = oldState is WindowState.Maximized or WindowState.FullScreen;
        }
    }

    // Size and state change notifications can arrive in either order when maximizing, so the capture is kept as
    // pending and confirmed once both have been processed (or when the state leaves Normal, see above).
    private void CaptureNormalLater()
    {
        if (_window.WindowState != WindowState.Normal)
        {
            return;
        }

        var size = _window.ClientSize;
        _normalBeforePending = (_normalPosition, _normalSize);
        _pending = (_window.Position, size.Width > 0 && size.Height > 0 ? size : _normalSize);
        Dispatcher.UIThread.Post(CaptureNormal, DispatcherPriority.Background);
    }

    private bool LooksMaximized(Size size)
    {
        var screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
        if (screen is null)
        {
            return false;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        return size.Width >= screen.WorkingArea.Width / scaling - 2 && size.Height >= screen.WorkingArea.Height / scaling - 64;
    }

    private void CaptureInitialNormal()
    {
        if (_restoredPlacement)
        {
            // Do not let Opened overwrite fitted restore bounds with a WM-adjusted Position (+5,+5 on xfwm4).
            _restoredPlacement = false;
            return;
        }

        CaptureNormal();
    }

    private void CaptureNormal()
    {
        if (_window.WindowState != WindowState.Normal)
        {
            return;
        }

        // Leave _pending / _normalBeforePending for OnWindowStateChanged so a frame nudge committed here
        // can still be rejected when maximizing.
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
