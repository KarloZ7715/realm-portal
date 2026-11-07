namespace RealmPortal.Tests.Support;

/// <summary>Runs blocking integration-test work without occupying a worker needed by the in-process server.</summary>
public static class DedicatedThread
{
    /// <summary>
    /// Await the returned task: exceptions still fail the test, and the action's own deadlines remain unchanged.
    /// LongRunning on the default scheduler creates a dedicated thread rather than another pool work item.
    /// </summary>
    public static Task Run(Action action) => Task.Factory.StartNew(
        action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
