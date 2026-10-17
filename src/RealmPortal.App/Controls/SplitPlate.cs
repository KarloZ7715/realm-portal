using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RealmPortal.App.Controls;

/// <summary>
/// The joined plate of a split button (kit pieces <c>split-{variant}-{state}</c>, 200x44 native, insets 16,14,58,14, tiled on x).
/// The plate is drawn in its normal state; a hovered or pressed segment is drawn again from the matching state plate,
/// clipped to that segment, so the shared seam stays continuous. The menu segment is the last 44 px (43.5 px of it
/// is visible, the pieces overlap by 1 px).
/// </summary>
public class SplitPlate : Control
{
    /// <summary>Width of the menu segment in DIPs, including the 1 px overlap.</summary>
    public const double MenuWidth = 43.5;

    private static readonly Thickness PlateInsets = new(16, 14, 58, 14);

    public static readonly StyledProperty<string> VariantProperty =
        AvaloniaProperty.Register<SplitPlate, string>(nameof(Variant), "primary");

    public static readonly StyledProperty<Control?> ActionSegmentProperty =
        AvaloniaProperty.Register<SplitPlate, Control?>(nameof(ActionSegment));

    public static readonly StyledProperty<Control?> MenuSegmentProperty =
        AvaloniaProperty.Register<SplitPlate, Control?>(nameof(MenuSegment));

    static SplitPlate()
    {
        AffectsRender<SplitPlate>(VariantProperty);
        ActionSegmentProperty.Changed.AddClassHandler<SplitPlate>((plate, args) => plate.Watch(args));
        MenuSegmentProperty.Changed.AddClassHandler<SplitPlate>((plate, args) => plate.Watch(args));
    }

    /// <summary><c>primary</c> or <c>secondary</c>.</summary>
    public string Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    /// <summary>The action (Play) button. Its <c>:pointerover</c>, <c>:pressed</c> and <c>:disabled</c> pseudo-classes pick the plate.</summary>
    public Control? ActionSegment { get => GetValue(ActionSegmentProperty); set => SetValue(ActionSegmentProperty, value); }

    /// <summary>The menu (chevron) button, read the same way.</summary>
    public Control? MenuSegment { get => GetValue(MenuSegmentProperty); set => SetValue(MenuSegmentProperty, value); }

    // Reading the pseudo-classes (not IsPointerOver) lets the gallery pin a state by adding the pseudo-class.
    private void Watch(AvaloniaPropertyChangedEventArgs args)
    {
        if (args.OldValue is Control old)
        {
            old.Classes.CollectionChanged -= OnClassesChanged;
        }

        if (args.NewValue is Control current)
        {
            current.Classes.CollectionChanged += OnClassesChanged;
        }

        InvalidateVisual();
    }

    private void OnClassesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private static bool Has(Control? control, string pseudoClass) => control?.Classes.Contains(pseudoClass) == true;

    public override void Render(DrawingContext context)
    {
        var scale = AssetBitmaps.ScaleOf(this);
        var full = new Rect(Bounds.Size);
        if (Has(ActionSegment, ":disabled"))
        {
            DrawPlate(context, "disabled", full, scale);
            return;
        }

        DrawPlate(context, "normal", full, scale);
        var seam = Math.Round((Bounds.Width - MenuWidth) * scale) / scale;
        DrawSegment(context, StateOf(ActionSegment), new Rect(0, 0, seam, Bounds.Height), full, scale);
        DrawSegment(context, StateOf(MenuSegment), new Rect(seam, 0, Bounds.Width - seam, Bounds.Height), full, scale);
    }

    internal static string? StateOf(Control? segment) =>
        Has(segment, ":pressed") ? "pressed" : Has(segment, ":pointerover") ? "hover" : null;

    private void DrawSegment(DrawingContext context, string? state, Rect segment, Rect full, double scale)
    {
        if (state is null)
        {
            return;
        }

        using var _ = context.PushClip(segment);
        DrawPlate(context, state, full, scale);
    }

    private void DrawPlate(DrawingContext context, string state, Rect destination, double scale)
    {
        var (bitmap, factor) = AssetBitmaps.Frame($"split-{Variant}-{state}", scale);
        NineSlice.Draw(context, bitmap, factor, PlateInsets, destination, scale, tileX: true);
    }
}
