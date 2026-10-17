using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RealmPortal.App.Dev;

/// <summary>Development-only component gallery: every themed control in every state. Open it with <c>--gallery</c>.</summary>
public partial class GalleryView : UserControl
{
    public GalleryView() => AvaloniaXamlLoader.Load(this);
}
