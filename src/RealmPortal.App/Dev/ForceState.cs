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

    /// <summary>
    /// Pins pseudo-classes on named parts of a control's template, for states that live on inner controls (the
    /// segments of a split button, the thumb of a scroll bar). Format: <c>Part=:state :state;OtherPart=:state</c>.
    /// </summary>
    public static readonly AttachedProperty<string?> PartStatesProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("PartStates", typeof(ForceState));

    static ForceState()
    {
        PartStatesProperty.Changed.AddClassHandler<Avalonia.Controls.Primitives.TemplatedControl>((control, args) =>
        {
            if (args.NewValue is string)
            {
                control.TemplateApplied += (_, e) => ApplyToParts(e.NameScope, GetPartStates(control));
            }
        });

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

    public static string? GetPartStates(Control control) => control.GetValue(PartStatesProperty);

    public static void SetPartStates(Control control, string? value) => control.SetValue(PartStatesProperty, value);

    private static void ApplyToParts(INameScope scope, string? partStates)
    {
        foreach (var entry in (partStates ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = entry.Split('=', 2);
            if (pair.Length == 2 && scope.Find<Control>(pair[0]) is { Classes: IPseudoClasses pseudoClasses })
            {
                foreach (var state in Split(pair[1]))
                {
                    pseudoClasses.Add(state);
                }
            }
        }
    }

    public static string? GetStates(Control control) => control.GetValue(StatesProperty);

    public static void SetStates(Control control, string? value) => control.SetValue(StatesProperty, value);

    private static string[] Split(string? states) =>
        string.IsNullOrWhiteSpace(states) ? [] : states.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
