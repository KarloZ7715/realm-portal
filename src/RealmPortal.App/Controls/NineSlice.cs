using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RealmPortal.App.Controls;

/// <summary>
/// Draws a 9-slice PNG from the designer's UI kit (assets/ui) behind its child.
/// <para>
/// <see cref="Insets"/> are the slice insets in @1x pixels (assets/ui/nineslice.json). By default the edge and centre
/// cells are stretched. With <see cref="TileX"/> / <see cref="TileY"/> they are tiled along that axis instead
/// (corners stay fixed; the other axis still stretches): leather buttons tile on x, tileable panels on both axes.
/// Tiles start at the cell's top-left (phase 0), as the kit's reference algorithm does.
/// </para>
/// <para>
/// <see cref="ShadowMargin"/> is only for art that has a shadow in the PNG (the kit's separate <c>shadow-soft</c> piece
/// has 12 px, the focus rings 8 px): the art is drawn that much outside the control's bounds, so the child and the layout
/// see only the body and templates need no negative margins. Nothing clips the overflow; keep the parent's
/// ClipToBounds off. Kit plates have no baked shadow, so they use 0.
/// </para>
/// <para>
/// Destination edges (and every tile edge) are snapped to device pixels, so fractional layouts (125 %, 150 % scaling)
/// do not open seams between slices or tiles. All insets 0 draws the whole PNG stretched, which is how fixed-size
/// pieces (checkbox, radio, switch thumb) use this control.
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

    public static readonly StyledProperty<bool> TileXProperty =
        AvaloniaProperty.Register<NineSlice, bool>(nameof(TileX));

    public static readonly StyledProperty<bool> TileYProperty =
        AvaloniaProperty.Register<NineSlice, bool>(nameof(TileY));

    static NineSlice()
    {
        AffectsRender<NineSlice>(SourceProperty, InsetsProperty, ShadowMarginProperty, TileXProperty, TileYProperty);
    }

    /// <summary>Piece name without extension or @2x suffix, e.g. <c>button-primary-normal</c>.</summary>
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>Slice insets in @1x pixels (left, top, right, bottom), measured from the PNG edge.</summary>
    public Thickness Insets { get => GetValue(InsetsProperty); set => SetValue(InsetsProperty, value); }

    /// <summary>Width of the shadow baked into the PNG, in @1x pixels. The art overflows the bounds by this amount.</summary>
    public double ShadowMargin { get => GetValue(ShadowMarginProperty); set => SetValue(ShadowMarginProperty, value); }

    /// <summary>Tile the top, bottom and centre cells horizontally instead of stretching them.</summary>
    public bool TileX { get => GetValue(TileXProperty); set => SetValue(TileXProperty, value); }

    /// <summary>Tile the left, right and centre cells vertically instead of stretching them.</summary>
    public bool TileY { get => GetValue(TileYProperty); set => SetValue(TileYProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Source is null)
        {
            return;
        }

        var scale = AssetBitmaps.ScaleOf(this);
        var (bitmap, factor) = AssetBitmaps.Frame(Source, scale);
        var destination = new Rect(-ShadowMargin, -ShadowMargin, Bounds.Width + 2 * ShadowMargin, Bounds.Height + 2 * ShadowMargin);
        Draw(context, bitmap, factor, Insets, destination, scale, TileX, TileY);
    }

    /// <summary>Draws the nine slices; shared with <see cref="SplitPlate"/> and the tests.</summary>
    internal static void Draw(DrawingContext context, Bitmap bitmap, int factor, Thickness insets, Rect destination, double scale,
        bool tileX = false, bool tileY = false)
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
                if (source.Width <= 0 || source.Height <= 0 || target.Width <= 0 || target.Height <= 0)
                {
                    continue;
                }

                // Only the middle column can tile on x, only the middle row on y.
                DrawCell(context, bitmap, source, target, factor, scale, tileX && column == 1, tileY && row == 1);
            }
        }
    }

    /// <summary>Draws one cell, repeating the source at its native size along the tiled axes (phase 0 at the cell's top-left).</summary>
    private static void DrawCell(DrawingContext context, Bitmap bitmap, Rect source, Rect target, int factor, double scale, bool tileX, bool tileY)
    {
        if (!tileX && !tileY)
        {
            context.DrawImage(bitmap, source, target);
            return;
        }

        var tileWidth = tileX ? source.Width / factor : target.Width;
        var tileHeight = tileY ? source.Height / factor : target.Height;
        for (var y = 0.0; y < target.Height - 1e-6; y += tileHeight)
        {
            var heightFraction = Math.Min(tileHeight, target.Height - y) / tileHeight;
            var top = Math.Round((target.Top + y) * scale) / scale;
            var bottom = tileY ? Math.Round(Math.Min(target.Top + y + tileHeight, target.Bottom) * scale) / scale : target.Bottom;
            for (var x = 0.0; x < target.Width - 1e-6; x += tileWidth)
            {
                var widthFraction = Math.Min(tileWidth, target.Width - x) / tileWidth;
                var leftEdge = Math.Round((target.Left + x) * scale) / scale;
                var rightEdge = tileX ? Math.Round(Math.Min(target.Left + x + tileWidth, target.Right) * scale) / scale : target.Right;
                var part = new Rect(source.X, source.Y, source.Width * widthFraction, source.Height * heightFraction);
                context.DrawImage(bitmap, part, new Rect(leftEdge, top, rightEdge - leftEdge, bottom - top));
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
