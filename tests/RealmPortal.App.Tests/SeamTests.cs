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
    // (frame, insets, shadow margin, size of the body in DIP, safe distance from the body edge in DIP)
    public static TheoryData<string, double, double, double, double, double> Frames => new()
    {
        { "panel-sheet-wide", 36, 12, 413.3, 91.7, 14 },
        { "panel-active-medium", 36, 12, 357.5, 263.2, 14 },
        { "button-primary-normal", 14, 0, 203.6, 40, 8 },
        { "button-secondary-hover", 14, 0, 97.4, 36, 8 },
        { "tab-parchment", 18, 10, 207.3, 44, 8 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Frames))]
    public void NoGapsInsideTheBody(string frame, double inset, double shadow, double width, double height, double safe)
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            var onMagenta = Render(frame, inset, shadow, width, height, scale, Brushes.Magenta);
            var onBlack = Render(frame, inset, shadow, width, height, scale, Brushes.Black);
            var left = (int)Math.Ceiling((40 + safe) * scale);
            var top = (int)Math.Ceiling((40 + safe) * scale);
            var right = (int)((40 + width - safe) * scale);
            var bottom = (int)((40 + height - safe) * scale);
            var differing = 0;
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
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

    private static Pixels Render(string frame, double inset, double shadow, double width, double height, double scale, IBrush background)
    {
        var slice = new NineSlice
        {
            Source = frame,
            Insets = new Thickness(inset),
            ShadowMargin = shadow,
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
