using Avalonia;
using Avalonia.Controls;

namespace RealmPortal.App.Dev;

/// <summary>
/// Development helper for the component gallery: pins pseudo-classes (":pointerover", ":pressed", ":focus-visible", ...)
/// on a control so every visual state can be shown at once. Space-separated, e.g. <c>dev:ForceState.States=":pointerover"</c>.
/// </summary>
public static class ForceState
{
    public static readonly AttachedProperty<string?> StatesProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("States", typeof(ForceState));

    static ForceState()
    {
        StatesProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (control.Classes is not IPseudoClasses pseudoClasses)
            {
                return;
            }

            foreach (var state in Split(args.OldValue as string))
            {
                pseudoClasses.Remove(state);
            }

            foreach (var state in Split(args.NewValue as string))
            {
                pseudoClasses.Add(state);
            }
        });
    }

    public static string? GetStates(Control control) => control.GetValue(StatesProperty);

    public static void SetStates(Control control, string? value) => control.SetValue(StatesProperty, value);

    private static string[] Split(string? states) =>
        string.IsNullOrWhiteSpace(states) ? [] : states.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
