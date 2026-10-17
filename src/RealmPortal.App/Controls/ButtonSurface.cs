using Avalonia;
using Avalonia.Controls;

namespace RealmPortal.App.Controls;

/// <summary>Background the button is drawn on. Drives border and focus-ring art (Figma Button surface variant).</summary>
public enum ButtonSurfaceKind
{
    Leather,
    Parchment,
}

/// <summary>
/// Attached <see cref="Surface"/> on <see cref="Button"/> and <see cref="SplitButton"/>: leather (default) or parchment.
/// Combines with the type style classes primary (default), <c>secondary</c>, and <c>ghost</c> (text).
/// </summary>
public static class ButtonSurface
{
    public static readonly AttachedProperty<ButtonSurfaceKind> SurfaceProperty =
        AvaloniaProperty.RegisterAttached<Control, ButtonSurfaceKind>(
            "Surface",
            typeof(ButtonSurface),
            ButtonSurfaceKind.Leather,
            inherits: true);

    static ButtonSurface()
    {
        SurfaceProperty.Changed.AddClassHandler<Button>(OnSurfaceChanged);
        SurfaceProperty.Changed.AddClassHandler<SplitButton>(OnSurfaceChanged);
    }

    public static ButtonSurfaceKind GetSurface(Control control) => control.GetValue(SurfaceProperty);

    public static void SetSurface(Control control, ButtonSurfaceKind value) => control.SetValue(SurfaceProperty, value);

    private static void OnSurfaceChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.Classes.Set("surface-parchment", GetSurface(control) == ButtonSurfaceKind.Parchment);
    }
}
