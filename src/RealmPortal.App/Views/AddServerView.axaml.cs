using Avalonia.Controls;
using Avalonia.Input;
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
        if (NameBox is not null)
        {
            NameBox.GotFocus += OnNameGotFocus;
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Wire GotFocus after InitializeComponent in case NameBox was null in ctor order.
        NameBox.GotFocus -= OnNameGotFocus;
        NameBox.GotFocus += OnNameGotFocus;

        // Duplicate (and add/edit): focus the name so the user can rename immediately.
        // In duplicate mode SelectAll so the next key replaces the whole " (copy)" / " (copia)" name (#191).
        Dispatcher.UIThread.Post(FocusNameForEdit, DispatcherPriority.Input);
    }

    private void OnNameGotFocus(object? sender, GotFocusEventArgs e) => SelectNameIfDuplicating();

    private void FocusNameForEdit()
    {
        if (NameBox is null)
        {
            return;
        }

        NameBox.Focus();
        SelectNameIfDuplicating();
        // Binding can refresh Text after focus; select again on the next input pass.
        Dispatcher.UIThread.Post(SelectNameIfDuplicating, DispatcherPriority.Input);
    }

    private void SelectNameIfDuplicating()
    {
        if (NameBox is null || DataContext is not AddServerViewModel { IsDuplicating: true })
        {
            return;
        }

        if (string.IsNullOrEmpty(NameBox.Text))
        {
            return;
        }

        NameBox.SelectAll();
    }
}
