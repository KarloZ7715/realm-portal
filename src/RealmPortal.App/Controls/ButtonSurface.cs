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
/// Attached <see cref="Surface"/> on <see cref="Button"/>: leather (default) or parchment.
/// Combines with the type style classes primary (default), <c>secondary</c>, and <c>ghost</c> (text).
/// </summary>
public static class ButtonSurface
{
    public static readonly AttachedProperty<ButtonSurfaceKind> SurfaceProperty =
        AvaloniaProperty.RegisterAttached<Button, ButtonSurfaceKind>("Surface", typeof(ButtonSurface), ButtonSurfaceKind.Leather);

    static ButtonSurface()
    {
        SurfaceProperty.Changed.AddClassHandler<Button>((button, args) =>
        {
            button.Classes.Set("surface-parchment", (ButtonSurfaceKind)args.NewValue! == ButtonSurfaceKind.Parchment);
        });
    }

    public static ButtonSurfaceKind GetSurface(Button button) => button.GetValue(SurfaceProperty);

    public static void SetSurface(Button button, ButtonSurfaceKind value) => button.SetValue(SurfaceProperty, value);
}
