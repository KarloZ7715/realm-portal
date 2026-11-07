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
        NameBox.GotFocus += OnNameGotFocus;
        ExtraLinesBox.AddHandler(KeyDownEvent, OnExtraLinesKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
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

    /// <summary>
    /// Enter inserts a newline (AcceptsReturn). Ctrl+Enter saves, matching the Designer a11y note for #192.
    /// </summary>
    private void OnExtraLinesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.Control)
            return;
        if (DataContext is not AddServerViewModel vm)
            return;
        e.Handled = true;
        if (vm.SaveCommand.CanExecute(null))
            vm.SaveCommand.Execute(null);
    }
}
