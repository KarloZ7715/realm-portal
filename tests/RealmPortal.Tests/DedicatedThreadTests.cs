using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class DedicatedThreadTests
{
    [Fact]
    public async Task RunsTheActionOffTheThreadPool()
    {
        var caller = Environment.CurrentManagedThreadId;

        await DedicatedThread.Run(() =>
        {
            Assert.False(Thread.CurrentThread.IsThreadPoolThread);
            Assert.NotEqual(caller, Environment.CurrentManagedThreadId);
        });
    }

    [Fact]
    public async Task AnActionFailureStillFailsTheReturnedTask()
    {
        var failure = new InvalidOperationException("synthetic failure");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DedicatedThread.Run(() => throw failure));

        Assert.Same(failure, actual);
    }
}
