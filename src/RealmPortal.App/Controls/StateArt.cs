using Avalonia;

namespace RealmPortal.App.Controls;

/// <summary>
/// A <see cref="NineSlice"/> whose piece name is built from the control state, for the kit's checkbox, radio and
/// switch-track art: <c>{Family}-{Surface}-{Value}-{state}</c> (for example <c>checkbox-leather-checked-hover</c>).
/// The state is <c>disabled</c>, else <c>hover</c>, else <c>normal</c>; the kit has no pressed art for these controls.
/// </summary>
public class StateArt : NineSlice
{
    public static readonly StyledProperty<string> FamilyProperty = AvaloniaProperty.Register<StateArt, string>(nameof(Family), "checkbox");
    public static readonly StyledProperty<string> SurfaceProperty = AvaloniaProperty.Register<StateArt, string>(nameof(Surface), "leather");
    public static readonly StyledProperty<string> ValueProperty = AvaloniaProperty.Register<StateArt, string>(nameof(Value), "unchecked");
    public static readonly StyledProperty<bool> IsHoverProperty = AvaloniaProperty.Register<StateArt, bool>(nameof(IsHover));
    public static readonly StyledProperty<bool> IsDimmedProperty = AvaloniaProperty.Register<StateArt, bool>(nameof(IsDimmed));

    static StateArt()
    {
        FamilyProperty.Changed.AddClassHandler<StateArt>((art, _) => art.Update());
        SurfaceProperty.Changed.AddClassHandler<StateArt>((art, _) => art.Update());
        ValueProperty.Changed.AddClassHandler<StateArt>((art, _) => art.Update());
        IsHoverProperty.Changed.AddClassHandler<StateArt>((art, _) => art.Update());
        IsDimmedProperty.Changed.AddClassHandler<StateArt>((art, _) => art.Update());
    }

    public StateArt() => Update();

    /// <summary><c>checkbox</c>, <c>radio</c> or <c>switch-track</c>.</summary>
    public string Family { get => GetValue(FamilyProperty); set => SetValue(FamilyProperty, value); }

    /// <summary><c>leather</c> or <c>parchment</c>.</summary>
    public string Surface { get => GetValue(SurfaceProperty); set => SetValue(SurfaceProperty, value); }

    /// <summary>checkbox: unchecked, checked, indeterminate; radio and switch: off, on.</summary>
    public string Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public bool IsHover { get => GetValue(IsHoverProperty); set => SetValue(IsHoverProperty, value); }

    public bool IsDimmed { get => GetValue(IsDimmedProperty); set => SetValue(IsDimmedProperty, value); }

    private void Update() =>
        Source = $"{Family}-{Surface}-{Value}-{(IsDimmed ? "disabled" : IsHover ? "hover" : "normal")}";
}
