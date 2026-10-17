using Avalonia.Controls;
using Avalonia.Input;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Views;

public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView()
    {
        InitializeComponent();
        Loaded += (_, _) => this.FindControl<Button>("CancelButton")?.Focus();
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
