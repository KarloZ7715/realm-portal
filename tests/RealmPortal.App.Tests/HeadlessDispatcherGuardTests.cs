using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace RealmPortal.App.Tests;

/// <summary>The guard in <see cref="TestApp.EnsureRunLoopDispatcher"/> (#217).</summary>
public class HeadlessDispatcherGuardTests
{
    private const string ReverifyReset =
        "Re-verify the dispatcher-reset reflection in TestApp.ResetUIThreadDispatcher before updating this guard.";

    [Fact]
    public void DispatcherReset_IsVerifiedAgainstThePinnedAvaloniaVersion()
    {
        var actual = typeof(Dispatcher).Assembly.GetName().Version;
        Assert.True(actual == new Version(11, 3, 22, 0),
            $"Expected Avalonia 11.3.22.0, loaded {actual}. {ReverifyReset}");
    }

    [Fact]
    public void DispatcherReset_HasTheExpectedInternalSignature()
    {
        var methods = typeof(Dispatcher).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => method.Name == "ResetForUnitTests").ToArray();
        Assert.True(methods.Length == 1,
            $"Expected exactly one static non-public ResetForUnitTests method, found {methods.Length}. {ReverifyReset}");
        var reset = methods[0];
        Assert.True(reset.ReturnType == typeof(void) && reset.GetParameters().Length == 0 && !reset.IsGenericMethod,
            $"Expected non-generic static void ResetForUnitTests() with no parameters, found {reset}. {ReverifyReset}");
    }

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
