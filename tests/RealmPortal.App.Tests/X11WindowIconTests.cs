using Avalonia.Headless.XUnit;
using RealmPortal.App.Platform;

namespace RealmPortal.App.Tests;

public class X11WindowIconTests
{
    [Fact]
    public void BuildProperty_LaysOutWidthHeightAndPixelsForEachImage()
    {
        var property = X11WindowIcon.BuildProperty(
        [
            new WindowIconImage(2, 1, [0xFF112233, 0x00000000]),
            new WindowIconImage(1, 2, [0x80010203, 0xFFFFFFFF]),
        ]);

        Assert.Equal<uint>([2, 1, 0xFF112233, 0, 1, 2, 0x80010203, 0xFFFFFFFF], property);
    }

    [Fact]
    public void BuildProperty_RejectsAWrongPixelCount() =>
        Assert.Throws<ArgumentException>(() => X11WindowIcon.BuildProperty([new WindowIconImage(2, 2, [1, 2, 3])]));

    [Fact]
    public void PackArgb_HandlesBothByteOrders_AndRowPadding()
    {
        byte[] bgra = [0x33, 0x22, 0x11, 0xFF, 0, 0, 0, 0, /* padding */ 9, 9, 9, 9];
        byte[] rgba = [0x11, 0x22, 0x33, 0xFF, 0, 0, 0, 0, 9, 9, 9, 9];

        Assert.Equal<uint>([0xFF112233, 0], X11WindowIcon.PackArgb(bgra, 2, 1, 12, bgra: true, premultiplied: false));
        Assert.Equal<uint>([0xFF112233, 0], X11WindowIcon.PackArgb(rgba, 2, 1, 12, bgra: false, premultiplied: false));
    }

    [Fact]
    public void PackArgb_UndoesPremultiplication()
    {
        // 50% alpha, premultiplied colour (0x40, 0x20, 0x10) -> about (0x80, 0x40, 0x20)
        byte[] premul = [0x10, 0x20, 0x40, 0x80];

        var pixel = Assert.Single(X11WindowIcon.PackArgb(premul, 1, 1, 4, bgra: true, premultiplied: true));

        Assert.Equal(0x80u, pixel >> 24);
        Assert.InRange((pixel >> 16) & 0xFF, 0x7Fu, 0x81u);
        Assert.InRange((pixel >> 8) & 0xFF, 0x3Fu, 0x41u);
        Assert.InRange(pixel & 0xFF, 0x1Fu, 0x21u);
    }

    [AvaloniaFact]
    public void EveryBrandingSize_DecodesToItsOwnDimensions_WithTransparentCornersAndOpaqueCenter()
    {
        foreach (var size in X11WindowIcon.Sizes)
        {
            using var png = File.OpenRead(Repo.Path("assets", "branding", "icon", "png", $"realm-portal-{size}.png"));

            var image = X11WindowIcon.Decode(png);

            Assert.Equal(size, image.Width);
            Assert.Equal(size, image.Height);
            Assert.Equal(size * size, image.Argb.Length);
            Assert.Equal(0u, image.Argb[0] >> 24); // transparent corner
            Assert.Contains(image.Argb, p => p >> 24 == 0xFF); // opaque art
        }
    }

    [AvaloniaFact]
    public void BundledResources_CoverEverySize()
    {
        var images = X11WindowIcon.LoadBundled();

        Assert.Equal(X11WindowIcon.Sizes, images.Select(i => i.Width));
    }

    [AvaloniaFact]
    public void TrySet_OnAWindowWithoutAnX11Handle_DoesNothing()
    {
        var window = new MainWindow();
        window.Show();

        Assert.False(X11WindowIcon.TrySet(window));
    }
}
