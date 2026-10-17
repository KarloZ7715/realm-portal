using Avalonia.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Tests;

/// <summary>Holds simulated steps open so a test can look at (or photograph) a screen in the middle of a switch or launch.</summary>
internal sealed class StepHold
{
    private readonly Dictionary<StepKind, TaskCompletionSource> _gates = [];

    public StepHold(DemoBackend backend, params StepKind[] holding)
    {
        foreach (var step in holding)
        {
            _gates[step] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        backend.StepGate = (step, _) => _gates.TryGetValue(step, out var gate) ? gate.Task : Task.CompletedTask;
    }

    public void Release(StepKind step) => _gates[step].TrySetResult();

    public void ReleaseAll()
    {
        foreach (var gate in _gates.Values)
        {
            gate.TrySetResult();
        }
    }
}

/// <summary>A main window over a zero-delay demo backend, sized like the compact mockups' client area.</summary>
internal sealed class Shell
{
    public Shell(double width = 720, double height = 440)
    {
        // The expected texts are Spanish: tests do not depend on the machine's language.
        Loc.Instance.SetLanguage(Language.Es);
        Backend = new DemoBackend { TimeScale = 0 };
        Model = new MainViewModel(Backend, autoTick: false);
        Window = new MainWindow(Model) { Width = width, Height = height };
        Window.Show();
        Screenshots.Settle();
    }

    public DemoBackend Backend { get; }

    public MainViewModel Model { get; }

    public MainWindow Window { get; }

    public ServersViewModel Servers => Model.Servers;

    public void Snap(string name, params double[] scales)
    {
        foreach (var scale in scales.Length == 0 ? [1.0, 1.5, 2.0] : scales)
        {
            Screenshots.Save(Window, scale, $"{name}-{scale:0.0}x.png");
        }

        Screenshots.SetRenderScaling(Window, 1.0);
        Screenshots.Settle();
    }
}
