using RealmPortal.App.ViewModels;
using RealmPortal.Core.Service;
using AppStepState = RealmPortal.App.Controls.StepState;

namespace RealmPortal.App.Portal;

internal static class ServiceProgressMapper
{
    public static readonly ServiceStep[] SwitchSteps = [ServiceStep.Plan, ServiceStep.WriteFiles, ServiceStep.Activate];

    public static readonly ServiceStep[] PlaySteps =
        [ServiceStep.Plan, ServiceStep.WriteFiles, ServiceStep.Activate, ServiceStep.Launch];

    public static int ProgressPercent(ServiceStep step, bool play)
    {
        var steps = play ? PlaySteps : SwitchSteps;
        var index = Array.IndexOf(steps, step);
        if (index < 0)
        {
            return 0;
        }

        var weights = play ? new[] { 15, 45, 75, 100 } : new[] { 20, 55, 100 };
        return weights[index];
    }

    public static void ApplyProgress(
        ServiceProgress progress,
        IList<StepViewModel> steps,
        ServiceStep[] kinds,
        ref int progressPercent)
    {
        var index = Array.IndexOf(kinds, progress.Step);
        if (index < 0)
        {
            return;
        }

        progressPercent = ProgressPercent(progress.Step, kinds.Length == PlaySteps.Length);
        switch (progress.State)
        {
            case StepState.Started:
                for (var i = 0; i < index; i++)
                {
                    steps[i].State = AppStepState.Done;
                }

                steps[index].State = AppStepState.Current;
                break;
            case StepState.Completed:
                steps[index].State = AppStepState.Done;
                break;
            case StepState.Failed:
                steps[index].State = AppStepState.Pending;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(progress));
        }
    }
}
