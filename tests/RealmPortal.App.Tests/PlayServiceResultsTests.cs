using RealmPortal.App.Service;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;

namespace RealmPortal.App.Tests;

public sealed class PlayServiceResultsTests
{
    [Fact]
    public void LaunchFailedWithAppliedLaunchFailed_RefreshesStatusAndKeepsUndo()
    {
        var error = new ServiceError(ServiceErrorCode.LaunchFailed, Consistency: Consistency.AppliedLaunchFailed);
        var result = ServiceResult<SwitchOutcome>.Fail(error);

        Assert.True(PlayServiceResults.ShouldRefreshStatusAfterFailure(result));
        Assert.True(PlayServiceResults.UndoRemainsAvailable(error));
    }

    [Fact]
    public void LaunchFailedWithUnchanged_DoesNotTreatSwitchAsApplied()
    {
        var error = new ServiceError(ServiceErrorCode.LaunchFailed, Consistency: Consistency.Unchanged);
        var result = ServiceResult<SwitchOutcome>.Fail(error);

        Assert.False(PlayServiceResults.ShouldRefreshStatusAfterFailure(result));
        Assert.False(PlayServiceResults.UndoRemainsAvailable(error));
    }

    [Fact]
    public void SuccessfulPlay_DoesNotNeedFailureRefresh()
    {
        var server = new ServerEntry("s1", "Realm", "host", "realm", "set realmlist host", null, 12340, null, true, true);
        var outcome = new SwitchOutcome(server, null, "b1", []);
        var result = ServiceResult<SwitchOutcome>.Ok(outcome);

        Assert.False(PlayServiceResults.ShouldRefreshStatusAfterFailure(result));
    }
}
