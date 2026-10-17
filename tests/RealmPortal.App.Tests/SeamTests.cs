using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using RealmPortal.App.Controls;

namespace RealmPortal.App.Tests;

/// <summary>
/// A seam between 9-slice pieces would let the background show through the opaque body of a frame.
/// Each frame is rendered over magenta and over black; inside the body (away from the torn/rounded edge and the shadow)
/// both renders must be identical, at every render scale, including fractional ones where slice edges do not fall on
/// whole device pixels.
/// </summary>
public class SeamTests
{
    // frame, insets (left, top, right, bottom), tile x, tile y, size of the body in DIP, safe distance from the body edge in DIP
    public static TheoryData<string, double, double, double, double, bool, bool, double, double, double> Frames => new()
    {
        { "panel-tile-normal", 20, 20, 20, 20, true, true, 413.3, 191.7, 12 },
        { "panel-tile-selected-hover", 20, 20, 20, 20, true, true, 357.5, 263.2, 12 },
        { "panel-tile-hover", 20, 20, 20, 20, true, true, 940, 150, 12 },
        { "panel-thin-normal", 20, 20, 20, 20, false, false, 301.3, 64, 12 },
        { "panel-thin-selected", 20, 20, 20, 20, false, false, 413.3, 56.6, 12 },
        { "button-primary-normal", 17, 14, 17, 14, true, false, 203.6, 40, 8 },
        { "button-primary-hover", 17, 14, 17, 14, true, false, 160, 40, 8 },
        { "button-primary-pressed", 17, 14, 17, 14, true, false, 251.4, 44, 8 },
        { "button-secondary-hover", 17, 14, 17, 14, true, false, 97.4, 36, 8 },
        { "split-primary-normal", 16, 14, 58, 14, true, false, 251.4, 44, 8 },
        { "split-secondary-pressed", 16, 14, 58, 14, true, false, 200, 44, 8 },
        { "tab-parchment-flat", 18, 18, 18, 18, false, false, 207.3, 44, 8 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Frames))]
    public void NoGapsInsideTheBody(string frame, double left, double top, double right, double bottom, bool tileX, bool tileY, double width, double height, double safe)
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            var insets = new Thickness(left, top, right, bottom);
            var onMagenta = Render(frame, insets, tileX, tileY, width, height, scale, Brushes.Magenta);
            var onBlack = Render(frame, insets, tileX, tileY, width, height, scale, Brushes.Black);
            var first = (int)Math.Ceiling((40 + safe) * scale);
            var last = (int)((40 + width - safe) * scale);
            var upper = (int)Math.Ceiling((40 + safe) * scale);
            var lower = (int)((40 + height - safe) * scale);
            var differing = 0;
            for (var y = upper; y < lower; y++)
            {
                for (var x = first; x < last; x++)
                {
                    if (Differs(onMagenta.Pixel(x, y), onBlack.Pixel(x, y)))
                    {
                        differing++;
                    }
                }
            }

            Assert.True(differing == 0, $"{frame} @{scale}x: {differing} body pixels depend on the background (seam)");
        }
    }

    [AvaloniaTheory]
    [InlineData("button-primary-normal", 160, 40)]
    [InlineData("split-primary-normal", 200, 44)]
    [InlineData("panel-tile-normal", 296, 296)]
    public void TheNativeSizeReproducesThePieceExactly(string frame, double width, double height)
    {
        var insets = frame.StartsWith("split") ? new Thickness(16, 14, 58, 14) : frame.StartsWith("button") ? new Thickness(17, 14, 17, 14) : new Thickness(20);
        var pixels = Render(frame, insets, true, frame.StartsWith("panel"), width, height, 1.0, Brushes.Black);

        var (bitmap, _) = AssetBitmaps.Frame(frame, 1.0);
        var source = new byte[(int)width * (int)height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(source, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new Avalonia.PixelRect(0, 0, (int)width, (int)height), handle.AddrOfPinnedObject(), source.Length, (int)width * 4);
        }
        finally
        {
            handle.Free();
        }

        var worst = 0;
        for (var y = 0; y < (int)height; y++)
        {
            for (var x = 0; x < (int)width; x++)
            {
                var rendered = pixels.Pixel(40 + x, 40 + y);
                var offset = (y * (int)width + x) * 4;
                // The bitmap is RGBA premultiplied, the capture is BGRA over black: both are colour x alpha, so they compare directly.
                for (var channel = 0; channel < 3; channel++)
                {
                    worst = Math.Max(worst, Math.Abs(rendered[channel] - source[offset + 2 - channel]));
                }
            }
        }

        Assert.True(worst <= 3, $"{frame}: native render differs from the PNG by up to {worst}/255");
    }

    /// <summary>Blending can differ by a level or two from rounding; a real gap differs by dozens.</summary>
    private static bool Differs(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        for (var i = 0; i < 3; i++)
        {
            if (Math.Abs(first[i] - second[i]) > 3)
            {
                return true;
            }
        }

        return false;
    }

    private static Pixels Render(string frame, Thickness insets, bool tileX, bool tileY, double width, double height, double scale, IBrush background)
    {
        var slice = new NineSlice
        {
            Source = frame,
            Insets = insets,
            TileX = tileX,
            TileY = tileY,
            Width = width,
            Height = height,
            Margin = new Thickness(40),
        };
        var window = new Window { Width = width + 80, Height = height + 80, Content = new Border { Background = background, Child = slice } };
        window.Show();
        using var bitmap = Screenshots.Capture(window, scale);
        using var locked = bitmap.Lock();
        var bytes = new byte[locked.RowBytes * locked.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
        window.Close();
        return new Pixels(bytes, locked.RowBytes);
    }

    private sealed record Pixels(byte[] Bytes, int RowBytes)
    {
        public ReadOnlySpan<byte> Pixel(int x, int y) => Bytes.AsSpan(y * RowBytes + x * 4, 3);
    }
}
