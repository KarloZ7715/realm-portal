using CommunityToolkit.Mvvm.ComponentModel;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;

namespace RealmPortal.App.ViewModels;

/// <summary>One numbered step of a switch or launch ("1 · Copia").</summary>
public sealed partial class StepViewModel(int number, StepKind kind, bool isLast) : ObservableObject
{
    public int Number { get; } = number;

    public StepKind Kind { get; } = kind;

    /// <summary>Width of the step's slot: the connector fills what the label leaves free (the last step has none).</summary>
    public double SlotWidth { get; } = isLast ? 150 : 200;

    public bool ShowConnector { get; } = !isLast;

    [ObservableProperty]
    private StepState _state = StepState.Pending;

    public string Label => Loc.Format("step.label", Number, Loc.Get($"step.{Kind.ToString().ToLowerInvariant()}"));

    public void RefreshText() => OnPropertyChanged(nameof(Label));
}
