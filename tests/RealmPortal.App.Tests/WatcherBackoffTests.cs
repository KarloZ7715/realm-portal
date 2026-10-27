using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Tests.Support;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;
using TempDirectory = RealmPortal.Tests.Support.TempDirectory;

namespace RealmPortal.App.Tests;

/// <summary>Game watcher probe interval backs off after the process is seen (#161).</summary>
public class WatcherBackoffTests
{
    private sealed class FakeLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec)
        {
        }
    }

    /// <summary>Reports whatever <see cref="Running"/> is set to; counts every probe.</summary>
    private sealed class MutableProbe : IGameProcessProbe
    {
        private int _calls;

        public bool Running { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public bool IsGameRunning(string clientRoot)
        {
            Interlocked.Increment(ref _calls);
            return Running;
        }
    }

    /// <summary>Records each requested delay and completes only when the test advances it (no wall-clock sleep for the interval).</summary>
    private sealed class StepDelay
    {
        private readonly ConcurrentQueue<TaskCompletionSource> _waiters = new();
        private readonly List<TimeSpan> _requested = [];
        private readonly object _gate = new();

        public IReadOnlyList<TimeSpan> Requested
        {
            get
            {
                lock (_gate)
                {
                    return _requested.ToArray();
                }
            }
        }

        public Task Invoke(TimeSpan interval)
        {
            lock (_gate)
            {
                _requested.Add(interval);
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(tcs);
            return tcs.Task;
        }

        public async Task AdvanceAsync(int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                TaskCompletionSource? tcs = null;
                // CI runners can be slower to schedule the Avalonia UI continuation than a local box.
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (!_waiters.TryDequeue(out tcs) && DateTime.UtcNow < deadline)
                {
                    // The watcher continues on the UI thread after Task.Run; pump so it can reach _delay.
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(5).ConfigureAwait(true);
                }

                Assert.NotNull(tcs);
                tcs!.TrySetResult();
                Dispatcher.UIThread.RunJobs();
            }
        }

        /// <summary>Pumps until the watcher has requested a delay that is still waiting.</summary>
        public async Task WaitUntilPendingAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (_waiters.IsEmpty && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(5).ConfigureAwait(true);
            }

            Assert.False(_waiters.IsEmpty, "expected an in-flight watcher delay");
        }

        public bool HasPending => !_waiters.IsEmpty;
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env(IGameProcessProbe probe, Func<TimeSpan, Task> delay)
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Client = _temp.Combine("client");
            Backups = _temp.Combine("backups");
            Home = _temp.Combine("home");
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(Projection);
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Home);
            Session = TestSessions.Create(
                Config,
                Projection,
                Backups,
                Home,
                new FakeLaunch(),
                probe,
                seedPreferences: false);
            var clientId = Session.Service.AddClient(Client).Value!.Id;
            Session.UiPreferences.Save(new UiPreferences("en", [clientId], FirstScanCompleted: true));
            Loc.Instance.SetLanguage(Language.En);
            Main = new MainViewModel(Session, () => Task.FromResult<string?>(Client), autoTick: false, delay: delay);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Client { get; }
        public string Backups { get; }
        public string Home { get; }
        public PortalSession Session { get; }
        public MainViewModel Main { get; }

        public ServerEntry AddServer(string name, string host) =>
            Session.Service.AddServer(new NewServer(name, host)).Value!;

        public void Dispose() => _temp.Dispose();
    }

    private static async Task PumpUntilAsync(Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5).ConfigureAwait(true);
        }
    }

    [AvaloniaFact]
    public async Task Watcher_UsesStartupIntervalUntilRunning_ThenBacksOff()
    {
        var probe = new MutableProbe();
        var delay = new StepDelay();
        using var env = new Env(probe, delay.Invoke);
        var server = env.AddServer("Play", "play.example.org");
        env.Main.Servers.ReloadFromService();
        var card = env.Main.Servers.All.Single(x => x.ServerId == server.Id);

        await env.Main.Servers.PlayAsync(card);

        // Two startup probes while the process has not appeared yet.
        await delay.AdvanceAsync(2);
        Assert.All(delay.Requested, t => Assert.Equal(ServersViewModel.StartupProbeInterval, t));

        probe.Running = true;
        // Next probe sees the process; the delay after that should already be the backed-off interval.
        await delay.AdvanceAsync(1);
        await delay.AdvanceAsync(2);

        var requested = delay.Requested;
        Assert.True(requested.Count >= 5);
        var firstRunning = -1;
        for (var i = 0; i < requested.Count; i++)
        {
            if (requested[i] == ServersViewModel.RunningProbeInterval)
            {
                firstRunning = i;
                break;
            }
        }

        Assert.True(firstRunning >= 0, "expected a running-interval delay after sawRunning");
        Assert.All(requested.Take(firstRunning), t => Assert.Equal(ServersViewModel.StartupProbeInterval, t));
        Assert.All(requested.Skip(firstRunning), t => Assert.Equal(ServersViewModel.RunningProbeInterval, t));
        Assert.InRange(ServersViewModel.RunningProbeInterval.TotalSeconds, 3, 5);
        Assert.Equal(ServersPhase.Running, env.Main.Servers.Phase);
        Assert.False(env.Main.Servers.GameClosed);

        // Stop the watcher: miss probes until GameClosed. Do not assume a fixed number of delays —
        // under load the in-flight delay may already be the first miss, so fewer waiters remain.
        probe.Running = false;
        var beforeClose = delay.Requested.Count;
        var closeDeadline = DateTime.UtcNow.AddSeconds(30);
        while (!env.Main.Servers.GameClosed && DateTime.UtcNow < closeDeadline)
        {
            if (delay.HasPending)
                await delay.AdvanceAsync(1);
            else
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(5).ConfigureAwait(true);
            }
        }

        await PumpUntilAsync(() => env.Main.Servers.GameClosed, TimeSpan.FromSeconds(2));
        Assert.True(env.Main.Servers.GameClosed);
        Assert.All(delay.Requested.Skip(beforeClose), t => Assert.Equal(ServersViewModel.RunningProbeInterval, t));
    }

    [AvaloniaFact]
    public async Task Watcher_ThreeMissesAtRunningInterval_MarksClosed()
    {
        var probe = new MutableProbe();
        var delay = new StepDelay();
        using var env = new Env(probe, delay.Invoke);
        var server = env.AddServer("Play", "play.example.org");
        env.Main.Servers.ReloadFromService();
        var card = env.Main.Servers.All.Single(x => x.ServerId == server.Id);

        // Start closed so Play is allowed; flip on once the watcher is waiting on its first delay.
        await env.Main.Servers.PlayAsync(card);
        probe.Running = true;
        var guard = 0;
        while (!delay.Requested.Contains(ServersViewModel.RunningProbeInterval) && guard++ < 10)
        {
            await delay.AdvanceAsync(1);
        }

        Assert.Contains(ServersViewModel.RunningProbeInterval, delay.Requested);
        Assert.Equal(ServersPhase.Running, env.Main.Servers.Phase);
        Assert.False(env.Main.Servers.GameClosed);

        // Sync on the in-flight running delay, then treat it as the first of the three
        // close-window intervals: that delay + two miss delays; the third miss closes
        // without requesting another.
        await delay.WaitUntilPendingAsync();
        var inFlightIndex = delay.Requested.Count - 1;
        Assert.Equal(ServersViewModel.RunningProbeInterval, delay.Requested[inFlightIndex]);

        probe.Running = false;
        await delay.AdvanceAsync(3);
        await PumpUntilAsync(() => env.Main.Servers.GameClosed, TimeSpan.FromSeconds(2));

        // Closed banner stays on the Running phase until the user hits Retry (Finish).
        Assert.True(env.Main.Servers.GameClosed);
        Assert.Equal(ServersPhase.Running, env.Main.Servers.Phase);
        var closing = delay.Requested.Skip(inFlightIndex).ToArray();
        Assert.Equal(3, closing.Length);
        Assert.All(closing, t => Assert.Equal(ServersViewModel.RunningProbeInterval, t));
        // 3 misses × 4 s cadence ⇒ exit reflected within ~12 s (≤ 15 s acceptance window).
        Assert.True(
            TimeSpan.FromTicks(ServersViewModel.RunningProbeInterval.Ticks * 3) <= TimeSpan.FromSeconds(15));
    }
}
