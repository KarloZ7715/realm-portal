using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RealmPortal.App.Controls;

/// <summary>
/// Draws a 9-slice PNG from the designer's pack behind its child.
/// <para>
/// <see cref="Insets"/> are the slice insets in @1x pixels (assets/frames/nineslice.json). Several frames have a soft
/// shadow baked into the PNG: <see cref="ShadowMargin"/> is that margin (panel 12, tab 10, focus ring 8). The art is
/// drawn that much outside the control's bounds, so the child and the layout see only the visible body and no negative
/// margins are needed in templates. Nothing clips the overflow; keep the parent's ClipToBounds off.
/// </para>
/// <para>
/// Destination edges are snapped to device pixels, so fractional layouts (125 %, 150 % scaling) do not open
/// seams between the nine slices.
/// </para>
/// </summary>
public class NineSlice : Decorator
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<NineSlice, string?>(nameof(Source));

    public static readonly StyledProperty<Thickness> InsetsProperty =
        AvaloniaProperty.Register<NineSlice, Thickness>(nameof(Insets));

    public static readonly StyledProperty<double> ShadowMarginProperty =
        AvaloniaProperty.Register<NineSlice, double>(nameof(ShadowMargin));

    static NineSlice()
    {
        AffectsRender<NineSlice>(SourceProperty, InsetsProperty, ShadowMarginProperty);
    }

    /// <summary>Frame name without extension or @2x suffix, e.g. <c>button-primary-normal</c>.</summary>
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>Slice insets in @1x pixels (left, top, right, bottom), measured from the PNG edge (shadow included).</summary>
    public Thickness Insets { get => GetValue(InsetsProperty); set => SetValue(InsetsProperty, value); }

    /// <summary>Width of the shadow baked into the PNG, in @1x pixels. The art overflows the bounds by this amount.</summary>
    public double ShadowMargin { get => GetValue(ShadowMarginProperty); set => SetValue(ShadowMarginProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Source is null)
        {
            return;
        }

        var scale = AssetBitmaps.ScaleOf(this);
        var (bitmap, factor) = AssetBitmaps.Frame(Source, scale);
        var destination = new Rect(-ShadowMargin, -ShadowMargin, Bounds.Width + 2 * ShadowMargin, Bounds.Height + 2 * ShadowMargin);
        Draw(context, bitmap, factor, Insets, destination, scale);
    }

    /// <summary>Draws the nine slices; shared with the tests so they can check the slice geometry.</summary>
    internal static void Draw(DrawingContext context, Bitmap bitmap, int factor, Thickness insets, Rect destination, double scale)
    {
        var pixelsWide = (double)bitmap.PixelSize.Width;
        var pixelsHigh = (double)bitmap.PixelSize.Height;

        var (left, right) = ShrinkToFit(insets.Left, insets.Right, destination.Width);
        var (top, bottom) = ShrinkToFit(insets.Top, insets.Bottom, destination.Height);

        double[] sourceX = [0, insets.Left * factor, pixelsWide - insets.Right * factor, pixelsWide];
        double[] sourceY = [0, insets.Top * factor, pixelsHigh - insets.Bottom * factor, pixelsHigh];
        double[] destX = Snap([destination.Left, destination.Left + left, destination.Right - right, destination.Right], scale);
        double[] destY = Snap([destination.Top, destination.Top + top, destination.Bottom - bottom, destination.Bottom], scale);

        using var _ = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var source = new Rect(sourceX[column], sourceY[row], sourceX[column + 1] - sourceX[column], sourceY[row + 1] - sourceY[row]);
                var target = new Rect(destX[column], destY[row], destX[column + 1] - destX[column], destY[row + 1] - destY[row]);
                if (source.Width > 0 && source.Height > 0 && target.Width > 0 && target.Height > 0)
                {
                    context.DrawImage(bitmap, source, target);
                }
            }
        }
    }

    /// <summary>When the target is smaller than the two insets together, scales both down so they never overlap.</summary>
    private static (double First, double Second) ShrinkToFit(double first, double second, double available)
    {
        var total = first + second;
        if (total <= available || total <= 0)
        {
            return (first, second);
        }

        var factor = Math.Max(available, 0) / total;
        return (first * factor, second * factor);
    }

    private static double[] Snap(double[] edges, double scale)
    {
        var snapped = new double[edges.Length];
        for (var i = 0; i < edges.Length; i++)
        {
            snapped[i] = Math.Round(edges[i] * scale) / scale;
        }

        return snapped;
    }
}
