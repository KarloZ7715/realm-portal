using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RealmPortal.App.Controls;

/// <summary>
/// The kit's <c>progress-stripes-tile</c> (12x8, period 12) repeated horizontally over the progress fill. Animate
/// <see cref="Offset"/> from 0 to 12 to make the stripes run; the loop is seamless. Clipped to its bounds.
/// </summary>
public class StripeTile : Control
{
    /// <summary>Width of one tile in DIPs, which is also the animation period.</summary>
    public const double Period = 12;

    public static readonly StyledProperty<double> OffsetProperty =
        AvaloniaProperty.Register<StripeTile, double>(nameof(Offset));

    static StripeTile()
    {
        AffectsRender<StripeTile>(OffsetProperty);
        ClipToBoundsProperty.OverrideDefaultValue<StripeTile>(true);
    }

    /// <summary>Horizontal shift in DIPs; 0 and <see cref="Period"/> look the same.</summary>
    public double Offset { get => GetValue(OffsetProperty); set => SetValue(OffsetProperty, value); }

    public override void Render(DrawingContext context)
    {
        var scale = AssetBitmaps.ScaleOf(this);
        var (bitmap, factor) = AssetBitmaps.Frame("progress-stripes-tile", scale);
        var source = new Rect(bitmap.PixelSize.ToSizeWithDpi(96));
        var height = source.Height / factor;
        var top = Math.Round((Bounds.Height - height) / 2 * scale) / scale;

        using var _ = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
        for (var x = Offset - Period; x < Bounds.Width; x += Period)
        {
            var left = Math.Round(x * scale) / scale;
            var right = Math.Round((x + Period) * scale) / scale;
            context.DrawImage(bitmap, source, new Rect(left, top, right - left, height));
        }
    }
}
