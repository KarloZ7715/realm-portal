using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RealmPortal.App.Views;

public partial class ServersView : UserControl
{
    public ServersView() => InitializeComponent();

    /// <summary>
    /// Prefer opening downward; if there is not enough room above the status bar, open upward (#191).
    /// Target may still be unset during Opening when ShowAt is used — Opened always clamps.
    /// </summary>
    private void CardMenu_Opening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        flyout.VerticalOffset = 0;
        var target = flyout.Target as Control;
        if (target is null || TopLevel.GetTopLevel(target) is not Window window)
        {
            // Default downward; Opened will flip if it covers the status bar.
            flyout.Placement = PlacementMode.BottomEdgeAlignedRight;
            return;
        }

        ApplyPreferredPlacement(flyout, target, window);
    }

    /// <summary>After layout, keep the presenter bottom at or above the status-bar top.</summary>
    private void CardMenu_Opened(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => ClampMenuAboveStatusBar(flyout, flipIfNeeded: true), DispatcherPriority.Loaded);
    }

    private static void ApplyPreferredPlacement(MenuFlyout flyout, Control target, Window window)
    {
        var statusTop = StatusBarTop(window) ?? window.Bounds.Height;
        var targetBottom = target.TranslatePoint(new Point(0, target.Bounds.Height), window)?.Y
                           ?? window.Bounds.Height;
        // Full card menu (Play…Delete + hints) is tall; leave room above the status bar.
        const double estimatedMenuHeight = 300;
        flyout.Placement = statusTop - targetBottom >= estimatedMenuHeight
            ? PlacementMode.BottomEdgeAlignedRight
            : PlacementMode.TopEdgeAlignedRight;
    }

    private static void ClampMenuAboveStatusBar(MenuFlyout flyout, bool flipIfNeeded)
    {
        var target = flyout.Target as Control;
        if (target is null || TopLevel.GetTopLevel(target) is not Window window)
        {
            return;
        }

        var statusTop = StatusBarTop(window);
        if (statusTop is null)
        {
            return;
        }

        var presenter = FindPresenter(window, target);
        if (presenter is null)
        {
            return;
        }

        var topLeft = presenter.TranslatePoint(default, window);
        if (topLeft is null)
        {
            return;
        }

        var bottom = topLeft.Value.Y + presenter.Bounds.Height;
        if (bottom <= statusTop.Value + 0.5)
        {
            return;
        }

        if (flipIfNeeded && flyout.Placement == PlacementMode.BottomEdgeAlignedRight)
        {
            flyout.Hide();
            flyout.Placement = PlacementMode.TopEdgeAlignedRight;
            flyout.VerticalOffset = 0;
            flyout.ShowAt(target);
            // Opened will fire again and clamp without flipping in a loop.
            return;
        }

        // Slide up so the bottom clears the status bar (covers FlipY landing on the window edge).
        var overflow = bottom - statusTop.Value;
        flyout.VerticalOffset -= overflow;
        Dispatcher.UIThread.Post(() =>
        {
            var again = FindPresenter(window, target);
            var tl = again?.TranslatePoint(default, window);
            if (again is null || tl is null)
            {
                return;
            }

            var b = tl.Value.Y + again.Bounds.Height;
            if (b > statusTop.Value + 0.5)
            {
                flyout.VerticalOffset -= b - statusTop.Value;
            }
        }, DispatcherPriority.Render);
    }

    private static double? StatusBarTop(Window window)
    {
        var status = window.FindControl<Border>("StatusBar");
        return status?.TranslatePoint(default, window)?.Y;
    }

    private static MenuFlyoutPresenter? FindPresenter(Window window, Control target) =>
        window.GetVisualDescendants().OfType<MenuFlyoutPresenter>().FirstOrDefault()
        ?? TopLevel.GetTopLevel(target)?.GetVisualDescendants().OfType<MenuFlyoutPresenter>().FirstOrDefault();
}
