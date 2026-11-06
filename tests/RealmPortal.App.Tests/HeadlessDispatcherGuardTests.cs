using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace RealmPortal.App.Tests;

/// <summary>The guard in <see cref="TestApp.EnsureRunLoopDispatcher"/> (#217).</summary>
public class HeadlessDispatcherGuardTests
{
    [AvaloniaFact]
    public void HeadlessTests_RunOnADispatcherThatSupportsNestedLoops()
    {
        Assert.True(Dispatcher.UIThread.SupportsRunLoops);
        Assert.True(Dispatcher.UIThread.CheckAccess());
    }

    [Fact]
    public void AStrayDispatcherWithoutTheHeadlessPlatform_IsDetected()
    {
        // Outside a headless test no dispatcher implementation is registered: a dispatcher created now is the kind a
        // stray background read creates between two headless tests. The guard must notice it rather than let a test
        // fail later in PushFrame. The next headless test resets the dispatcher again.
        TestApp.ResetUIThreadDispatcher();
        try
        {
            Assert.False(Dispatcher.UIThread.SupportsRunLoops);
            var before = TestApp.StrayDispatchersReplaced;
            Assert.Throws<InvalidOperationException>(TestApp.EnsureRunLoopDispatcher);
            Assert.Equal(before + 1, TestApp.StrayDispatchersReplaced);
        }
        finally
        {
            TestApp.ResetUIThreadDispatcher();
        }
    }
}
