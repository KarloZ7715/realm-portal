using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace RealmPortal.App.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
    }

    private void OnBackFromClientsClick(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() => ContinueLanguageButton.Focus());

    private void OnBackFromServersClick(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() => BackClientsButton.Focus());
}
