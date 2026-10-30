using Avalonia.Controls;
using Avalonia.Threading;
using RealmPortal.Core.Instances;

namespace RealmPortal.App.Platform;

/// <summary>
/// Receives messages from later launches (#197) and brings the main window to the front. Messages arrive on a
/// background thread; before the main window exists a focus request is remembered and applied when it is attached.
/// </summary>
internal static class InstanceActivation
{
    private static readonly object Sync = new();
    private static Window? _window;
    private static WindowState _restoreState = WindowState.Normal;
    private static bool _pendingFocus;

    /// <summary>Channel handler: called on a background thread, must not block.</summary>
    public static InstanceReply Handle(InstanceMessage message)
    {
        if (message.Verb != InstanceMessage.FocusVerb)
            return InstanceReply.Unsupported;

        Window? window;
        lock (Sync)
        {
            window = _window;
            if (window is null)
            {
                _pendingFocus = true;
                return InstanceReply.Ok;
            }
        }

        Dispatcher.UIThread.Post(() => BringToFront(window));
        return InstanceReply.Ok;
    }

    /// <summary>Called on the UI thread once the main window is created.</summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Track(window);
        bool pending;
        lock (Sync)
        {
            _window = window;
            pending = _pendingFocus;
            _pendingFocus = false;
        }
        if (pending)
            Dispatcher.UIThread.Post(() => BringToFront(window));
    }

    /// <summary>
    /// Shows the window if hidden, restores it if minimized (to maximized when it was maximized before) and activates
    /// it. On X11, Avalonia sends <c>_NET_ACTIVE_WINDOW</c>; on Windows it calls <c>SetForegroundWindow</c>. The window
    /// manager may still decline and only mark the window as demanding attention (see docs/single-instance.md).
    /// </summary>
    internal static void BringToFront(Window window)
    {
        lock (Sync)
        {
            if (!ReferenceEquals(window, _window))
                return; // Closed meanwhile (the app is shutting down).
        }

        try
        {
            if (!window.IsVisible)
                window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = RestoreStateFor(window);
            window.Activate();
        }
        catch (InvalidOperationException)
        {
            // The window is closing; a focus request is best effort and must never crash the app.
        }
    }

    internal static WindowState RestoreStateFor(Window window)
    {
        lock (Sync)
            return ReferenceEquals(window, _window) ? _restoreState : WindowState.Normal;
    }

    private static void Track(Window window)
    {
        Remember(window.WindowState);
        window.Closed += (_, _) =>
        {
            lock (Sync)
            {
                if (ReferenceEquals(_window, window))
                    _window = null;
            }
        };
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty && e.NewValue is WindowState state)
                Remember(state);
        };
    }

    private static void Remember(WindowState state)
    {
        if (state == WindowState.Minimized)
            return;
        lock (Sync)
            _restoreState = state;
    }

    /// <summary>Test hook: forgets the attached window and any pending request.</summary>
    internal static void Reset()
    {
        lock (Sync)
        {
            _window = null;
            _pendingFocus = false;
            _restoreState = WindowState.Normal;
        }
    }
}
