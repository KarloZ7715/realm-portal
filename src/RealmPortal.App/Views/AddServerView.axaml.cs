using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Views;

public partial class AddServerView : UserControl
{
    public AddServerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Duplicate (and add/edit): focus the name so the user can rename immediately.
        // In duplicate mode the " (copy)" / " (copia)" name is selected for a one-key overwrite (#191 review).
        Dispatcher.UIThread.Post(() =>
        {
            if (NameBox is null)
            {
                return;
            }

            NameBox.Focus();
            if (DataContext is AddServerViewModel { IsDuplicating: true } && NameBox.Text is { Length: > 0 })
            {
                NameBox.SelectionStart = 0;
                NameBox.SelectionEnd = NameBox.Text.Length;
            }
        }, DispatcherPriority.Loaded);
    }
}
