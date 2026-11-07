using Avalonia.Controls;
using Avalonia.Input;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Views;

public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView()
    {
        InitializeComponent();
        // Tab navigation marks focus-visible so the Cancel focus ring is drawn (#196 design).
        Loaded += (_, _) => this.FindControl<Button>("CancelButton")?.Focus(NavigationMethod.Tab);
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not ConfirmDialogViewModel vm)
        {
            return;
        }

        vm.CancelCommand.Execute(null);
        e.Handled = true;
    }
}
