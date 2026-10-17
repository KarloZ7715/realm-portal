using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RealmPortal.App.Controls;

public enum StepState
{
    Pending,
    Current,
    Done,
}

/// <summary>
/// One step of a short progress sequence: a round marker, a label ("1 · Copy") and a dashed connector to the next step.
/// Put several in a one-row grid; the state shows as a check (done), a ringed dot (current) or an empty marker (pending).
/// </summary>
public class StepIndicator : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<StepIndicator, string?>(nameof(Label));

    public static readonly StyledProperty<StepState> StateProperty =
        AvaloniaProperty.Register<StepIndicator, StepState>(nameof(State));

    public static readonly StyledProperty<bool> ShowConnectorProperty =
        AvaloniaProperty.Register<StepIndicator, bool>(nameof(ShowConnector), true);

    public StepIndicator() => UpdateClasses();

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public StepState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>False on the last step.</summary>
    public bool ShowConnector { get => GetValue(ShowConnectorProperty); set => SetValue(ShowConnectorProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            UpdateClasses();
        }
    }

    private void UpdateClasses()
    {
        PseudoClasses.Set(":done", State == StepState.Done);
        PseudoClasses.Set(":current", State == StepState.Current);
        PseudoClasses.Set(":pending", State == StepState.Pending);
    }
}
