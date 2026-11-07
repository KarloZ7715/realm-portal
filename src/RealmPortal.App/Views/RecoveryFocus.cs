using Avalonia.Controls;
using Avalonia.Threading;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Views;

internal static class RecoveryFocus
{
    public static void Wire(UserControl view, Func<string, string> controlName)
    {
        ViewModelBase? current = null;
        void Focus(string field, int? line)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var control = view.FindControl<Control>(controlName(field));
                if (control is null) return;
                control.BringIntoView();
                control.Focus();
                if (control is TextBox box && line is { } index)
                {
                    var lines = (box.Text ?? string.Empty).Split('\n');
                    var start = lines.Take(Math.Clamp(index, 0, lines.Length)).Sum(text => text.Length + 1);
                    box.SelectionStart = Math.Min(start, box.Text?.Length ?? 0);
                    box.SelectionEnd = Math.Min(start + lines.ElementAtOrDefault(index)?.Length ?? start, box.Text?.Length ?? 0);
                }
            }, DispatcherPriority.Loaded);
        }
        void Update()
        {
            if (current is not null) current.RecoveryFocusRequested -= Focus;
            current = view.DataContext as ViewModelBase;
            if (current is not null) current.RecoveryFocusRequested += Focus;
        }
        view.DataContextChanged += (_, _) => Update();
        view.AttachedToVisualTree += (_, _) => Update();
        view.DetachedFromVisualTree += (_, _) =>
        {
            if (current is not null) current.RecoveryFocusRequested -= Focus;
            current = null;
        };
    }
}
