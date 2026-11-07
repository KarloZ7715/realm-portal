using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Views;

public partial class AddServerView : UserControl
{
    public AddServerView()
    {
        InitializeComponent();
        ExtraLinesBox.AddHandler(KeyDownEvent, OnExtraLinesKeyDown, RoutingStrategies.Tunnel);
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
