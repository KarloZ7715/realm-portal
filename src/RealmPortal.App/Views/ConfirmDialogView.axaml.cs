using Avalonia.Controls;

namespace RealmPortal.App.Views;

public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView()
    {
        InitializeComponent();
        Loaded += (_, _) => this.FindControl<Button>("CancelButton")?.Focus();
    }
}
