using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RealmPortal.App.Dev;

namespace RealmPortal.App.Tests;

public class GalleryScreenshotTests
{
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void GalleryRendersAtEachScale(double scale)
    {
        var view = new GalleryView();
        var window = new Window { Width = 1100, Height = 800, Content = view };
        window.Show();
        Screenshots.Settle();
        view.Measure(new Avalonia.Size(1100, double.PositiveInfinity));
        window.Height = Math.Ceiling(view.DesiredSize.Height);
        using var frame = Screenshots.Capture(window, scale);
        Assert.Equal(scale, window.RenderScaling);
        Assert.True(frame.PixelSize.Width >= 1100 * scale - 1);
        frame.Save(Path.Combine(Screenshots.OutputDirectory, $"gallery-{scale:0.0}x.png"));
    }
}
