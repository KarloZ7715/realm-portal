using Avalonia.Controls;

namespace RealmPortal.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        RecoveryFocus.Wire(this, field => field);
    }
}
