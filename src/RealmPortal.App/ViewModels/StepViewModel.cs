using CommunityToolkit.Mvvm.ComponentModel;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.Core.Service;
using AppStepState = RealmPortal.App.Controls.StepState;

namespace RealmPortal.App.ViewModels;

/// <summary>One numbered step of a switch or launch.</summary>
public sealed partial class StepViewModel : ObservableObject
{
    private readonly StepKind? _demoKind;
    private readonly ServiceStep? _serviceStep;

    public StepViewModel(int number, StepKind kind, bool isLast)
    {
        Number = number;
        _demoKind = kind;
        SlotWidth = isLast ? 150 : 200;
        ShowConnector = !isLast;
    }

    public StepViewModel(int number, ServiceStep step, bool isLast)
    {
        Number = number;
        _serviceStep = step;
        SlotWidth = isLast ? 150 : 200;
        ShowConnector = !isLast;
    }

    public int Number { get; }

    public StepKind? DemoKind => _demoKind;

    public ServiceStep? ServiceStep => _serviceStep;

    /// <summary>Width of the step's slot: the connector fills what the label leaves free (the last step has none).</summary>
    public double SlotWidth { get; }

    public bool ShowConnector { get; }

    [ObservableProperty]
    private AppStepState _state = AppStepState.Pending;

    public string Label => Loc.Format(
        "step.label",
        Number,
        _serviceStep is { } service
            ? Loc.Get($"servicestep.{service.ToString().ToLowerInvariant()}")
            : Loc.Get($"step.{_demoKind!.Value.ToString().ToLowerInvariant()}"));

    public void RefreshText() => OnPropertyChanged(nameof(Label));
}
