using Avalonia.Controls;

namespace RealmPortal.App.Dev;

/// <summary>Window that hosts the gallery in a scroll viewer (system title bar, like the real app).</summary>
public class GalleryWindow : Window
{
    public GalleryWindow()
    {
        Title = "Realm Portal - component gallery (development)";
        Width = 1180;
        Height = 860;
        Content = new ScrollViewer { Content = new GalleryView() };
    }
}
