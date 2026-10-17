using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Documents;

namespace RealmPortal.App.Controls;

/// <summary>
/// Draws one of the icon geometries from Theme/Icons.axaml (24x24 grid) with the inherited Foreground.
/// Because the colour is inherited, an icon inside a button follows the button's state colour.
/// </summary>
public class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<Icon, bool>(nameof(IsFilled));

    /// <summary>Stroke width on the 24 px grid; the icon pack uses 1.6 to 2.</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), 1.8);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, IsFilledProperty, StrokeThicknessProperty, ForegroundProperty);
        AffectsMeasure<Icon>(WidthProperty, HeightProperty);
    }

    public Icon()
    {
        Width = 16;
        Height = 16;
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    public bool IsFilled { get => GetValue(IsFilledProperty); set => SetValue(IsFilledProperty, value); }

    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Data is null || Foreground is null)
        {
            return;
        }

        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale));
        if (IsFilled)
        {
            context.DrawGeometry(Foreground, null, Data);
        }
        else
        {
            var pen = new Pen(Foreground, StrokeThickness, lineCap: PenLineCap.Flat, lineJoin: PenLineJoin.Miter);
            context.DrawGeometry(null, pen, Data);
        }
    }
}
