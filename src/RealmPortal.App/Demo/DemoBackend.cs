namespace RealmPortal.App.Demo;

/// <summary>What one step of a switch or launch does (only simulated in this build).</summary>
public enum StepKind
{
    Copy,
    Realmlist,
    Account,
    Launch,
}

/// <summary>
/// Simulated backend for the F3b screens: every step is a delay and nothing touches the disk, a client or the network.
/// <see cref="TimeScale"/> 0 makes the steps instant (tests); <see cref="StepGate"/> lets a test or a screenshot hold a step.
/// </summary>
public sealed class DemoBackend
{
    private TaskCompletionSource _gameExit = Completed();

    /// <summary>1 = real demo pace, 0 = no waiting.</summary>
    public double TimeScale { get; set; } = 1;

    public TimeSpan StepDuration { get; set; } = TimeSpan.FromMilliseconds(800);

    /// <summary>How long the simulated game stays open before it "closes" by itself (never when <see cref="TimeScale"/> is 0).</summary>
    public TimeSpan GameLifetime { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>When set, replaces the delay of a step: the step ends when the returned task completes.</summary>
    public Func<StepKind, CancellationToken, Task>? StepGate { get; set; }

    public bool GameRunning => !_gameExit.Task.IsCompleted;

    public Task GameExited => _gameExit.Task;

    public async Task RunStepAsync(StepKind step, CancellationToken cancellationToken = default)
    {
        if (StepGate is { } gate)
        {
            await gate(step, cancellationToken).ConfigureAwait(true);
            return;
        }

        await DelayAsync(StepDuration, cancellationToken).ConfigureAwait(true);
    }

    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default) =>
        TimeScale <= 0 ? Task.CompletedTask : Task.Delay(duration * TimeScale, cancellationToken);

    /// <summary>Pretends the game started.</summary>
    public void StartGame()
    {
        _gameExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (TimeScale > 0)
        {
            var exit = _gameExit;
            _ = Task.Delay(GameLifetime * TimeScale).ContinueWith(_ => exit.TrySetResult(), TaskScheduler.Default);
        }
    }

    /// <summary>Pretends the player closed the game.</summary>
    public void CloseGame() => _gameExit.TrySetResult();

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource();
        source.SetResult();
        return source;
    }
}
