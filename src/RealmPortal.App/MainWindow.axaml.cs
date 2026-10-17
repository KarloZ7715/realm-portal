using Avalonia.Controls;

namespace RealmPortal.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Platform.X11WindowIcon.TrySet(this);
    }
}
