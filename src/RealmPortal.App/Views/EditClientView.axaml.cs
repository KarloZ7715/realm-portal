using Avalonia.Controls;

namespace RealmPortal.App.Views;

public partial class EditClientView : UserControl
{
    public EditClientView()
    {
        InitializeComponent();
        RecoveryFocus.Wire(this, field => field.ToLowerInvariant() is "path" or "folder" ? "ClientFolderBox" : "ClientNameBox");
    }
}
