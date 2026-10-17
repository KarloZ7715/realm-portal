using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RealmPortal.App.Controls;

/// <summary>A thin bronze line that fills <see cref="Fraction"/> (0 to 1) of its width: the countdown under the undo notice.</summary>
public class CountdownLine : Control
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<CountdownLine, double>(nameof(Fraction), 1);

    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<CountdownLine, IBrush?>(nameof(Brush));

    static CountdownLine()
    {
        AffectsRender<CountdownLine>(FractionProperty, BrushProperty);
    }

    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var scale = AssetBitmaps.ScaleOf(this);
        var width = Math.Round(Bounds.Width * Math.Clamp(Fraction, 0, 1) * scale) / scale;
        if (width > 0 && Brush is not null)
        {
            context.DrawRectangle(Brush, null, new Rect(0, 0, width, Bounds.Height));
        }
    }
}
