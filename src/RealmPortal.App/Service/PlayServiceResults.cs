using RealmPortal.Core.Service;

namespace RealmPortal.App.Service;

/// <summary>How the UI should interpret <see cref="IPortalService.Play"/> results until the servers screen is fully wired to the service.</summary>
public static class PlayServiceResults
{
    /// <summary>True when the switch steps completed and only launch failed (<see cref="Consistency.AppliedLaunchFailed"/>).</summary>
    public static bool SwitchWasApplied(ServiceError error) =>
        error.Code == ServiceErrorCode.LaunchFailed && error.Consistency == Consistency.AppliedLaunchFailed;

    /// <summary>After Play fails: reload active server, undo affordances, and server list from <see cref="IPortalService.GetStatus"/>.</summary>
    public static bool ShouldRefreshStatusAfterFailure(ServiceResult<SwitchOutcome> result) =>
        !result.Succeeded && result.Error is { } error && SwitchWasApplied(error);

    /// <summary>Undo remains meaningful when the switch was applied even though launch failed.</summary>
    public static bool UndoRemainsAvailable(ServiceError error) => SwitchWasApplied(error);
}
