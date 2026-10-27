using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using RealmPortal.Core.Network;

namespace RealmPortal.Tests;

public class ProbeSchedulerTests
{
    [Fact]
    public async Task LoopbackListener_MapsToOnline()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probe = new TcpConnectionProbe((_, _) => Task.FromResult(new[] { IPAddress.Loopback }));
            using var scheduler = CreateScheduler(probe, jitter: _ => 0);
            var snap = await WaitForStatusAsync(scheduler, "s1", ServerProbeStatus.Online, () =>
            {
                scheduler.SetServers([new ProbeServerTarget("s1", $"127.0.0.1:{port}")]);
                scheduler.NotifyListVisible(true);
            });

            Assert.Equal(ServerProbeStatus.Online, snap.Status);
            Assert.Null(snap.Reason);
            Assert.NotNull(snap.CheckedAtUtc);
            Assert.Contains("Login server reachable", ProbeScheduler.FormatTooltip(snap, snap.CheckedAtUtc!.Value));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ClosedPort_MapsToOffline()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var probe = new TcpConnectionProbe((_, _) => Task.FromResult(new[] { IPAddress.Loopback }));
        using var scheduler = CreateScheduler(probe, jitter: _ => 0);
        var snap = await WaitForStatusAsync(scheduler, "s1", ServerProbeStatus.Offline, () =>
        {
            scheduler.SetServers([new ProbeServerTarget("s1", $"127.0.0.1:{port}")]);
            scheduler.NotifyListVisible(true);
        });

        Assert.Equal(ServerProbeStatus.Offline, snap.Status);
        Assert.False(string.IsNullOrWhiteSpace(snap.Reason));
    }

    [Fact]
    public async Task DnsFailure_IsUnknownNotOffline()
    {
        var probe = new ScriptedProbe(_ => ConnectionProbeResult.Failed(SocketError.HostNotFound));
        using var scheduler = CreateScheduler(probe, jitter: _ => 0);
        var snap = await WaitForStatusAsync(scheduler, "s1", ServerProbeStatus.Unknown, () =>
        {
            scheduler.SetServers([new ProbeServerTarget("s1", "missing.example")]);
            scheduler.NotifyListVisible(true);
        });

        Assert.Equal(ServerProbeStatus.Unknown, snap.Status);
        Assert.Equal(ProbeScheduler.ReasonDns, snap.Reason);
        Assert.Contains("name not found (DNS)", ProbeScheduler.FormatTooltip(snap, snap.CheckedAtUtc ?? DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MapResult_NoData_IsUnknown()
    {
        ProbeScheduler.MapResult(ConnectionProbeResult.Failed(SocketError.NoData), out var status, out var reason);
        Assert.Equal(ServerProbeStatus.Unknown, status);
        Assert.Equal(ProbeScheduler.ReasonDns, reason);
    }

    [Fact]
    public async Task NoProbesUntilListVisible()
    {
        var probe = new ScriptedProbe(_ => ConnectionProbeResult.Ok);
        using var scheduler = CreateScheduler(probe, jitter: _ => 0);
        scheduler.SetServers([new ProbeServerTarget("s1", "127.0.0.1")]);
        await Task.Delay(100);
        Assert.Equal(0, probe.Calls);

        scheduler.NotifyListVisible(true);
        await WaitForStatusAsync(scheduler, "s1", ServerProbeStatus.Online);
        Assert.True(probe.Calls >= 1);
    }

    [Fact]
    public async Task PerServerSingleFlight()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new ScriptedProbe(async ct =>
        {
            await gate.Task.WaitAsync(ct).ConfigureAwait(false);
            return ConnectionProbeResult.Ok;
        });
        using var scheduler = CreateScheduler(probe, jitter: _ => 0);
        scheduler.SetServers([new ProbeServerTarget("s1", "127.0.0.1")]);
        scheduler.NotifyListVisible(true);

        await WaitForAsync(() => probe.Calls == 1);
        scheduler.NotifyListVisible(true); // second kick must not start another probe
        await Task.Delay(50);
        Assert.Equal(1, probe.Calls);

        gate.TrySetResult();
        await WaitForStatusAsync(scheduler, "s1", ServerProbeStatus.Online);
    }

    [Fact]
    public async Task ConcurrencyCap_CountsUnfinishedOps()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var maxSeen = 0;
        var probe = new ScriptedProbe(async ct =>
        {
            var n = Interlocked.Increment(ref inFlight);
            while (true)
            {
                var seen = Volatile.Read(ref maxSeen);
                if (n <= seen || Interlocked.CompareExchange(ref maxSeen, n, seen) == seen)
                    break;
            }

            if (n >= ProbeScheduler.MaxConcurrent)
                started.TrySetResult();

            try
            {
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return ConnectionProbeResult.Ok;
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        });

        using var scheduler = CreateScheduler(probe, jitter: _ => 0);
        var targets = Enumerable.Range(0, 8)
            .Select(i => new ProbeServerTarget($"s{i}", "127.0.0.1"))
            .ToList();
        scheduler.SetServers(targets);
        scheduler.NotifyListVisible(true);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(scheduler.UnfinishedOperations <= ProbeScheduler.MaxConcurrent);
        Assert.True(maxSeen <= ProbeScheduler.MaxConcurrent);

        release.TrySetResult();
        await WaitForAsync(() => targets.All(t =>
            scheduler.TryGetStatus(t.ServerId, out var s) && s.Status == ServerProbeStatus.Online));
    }

    [Fact]
    public async Task FiveMinuteMinimum_IncludingListReopen()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var delays = new ControllableDelay();
        var probe = new ScriptedProbe(_ => ConnectionProbeResult.Ok);
        using var scheduler = CreateScheduler(probe, clock: clock.UtcNow, jitter: _ => 0, delay: delays.Delay);

        scheduler.SetServers([new ProbeServerTarget("s1", "127.0.0.1")]);
        scheduler.NotifyListVisible(true);
        await WaitForStatusAsync(scheduler, "s1", ServerProbeStatus.Online);
        Assert.Equal(1, probe.Calls);

        // Loop is waiting for the 5-minute floor (+jitter 0). Re-showing the list must not probe again yet.
        scheduler.NotifyListVisible(false);
        scheduler.NotifyListVisible(true);
        await Task.Delay(50);
        Assert.Equal(1, probe.Calls);

        clock.Advance(ProbeScheduler.MinInterval);
        delays.Release();
        await WaitForAsync(() => probe.Calls >= 2, TimeSpan.FromSeconds(5));
        Assert.True(probe.Calls >= 2);
    }

    [Fact]
    public async Task Disabling_CancelsInFlightAndResets()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new ScriptedProbe(async ct =>
        {
            await gate.Task.WaitAsync(ct).ConfigureAwait(false);
            return ConnectionProbeResult.Ok;
        });
        using var scheduler = CreateScheduler(probe, jitter: _ => 0);
        var sawChecking = false;
        scheduler.StatusChanged += snap =>
        {
            if (snap.ServerId == "s1" && snap.Status == ServerProbeStatus.Checking)
                sawChecking = true;
        };

        scheduler.SetServers([new ProbeServerTarget("s1", "127.0.0.1")]);
        scheduler.NotifyListVisible(true);
        await WaitForAsync(() => sawChecking && probe.Calls == 1);

        scheduler.SetEnabled(false);
        Assert.True(scheduler.TryGetStatus("s1", out var snap));
        Assert.Equal(ServerProbeStatus.Unknown, snap.Status);
        Assert.Null(snap.CheckedAtUtc);
        await Task.Delay(50);
        Assert.False(probe.SawCompleted); // cancelled before completion
        gate.TrySetCanceled();
    }

    [Fact]
    public void FormatTooltip_IncludesAge()
    {
        var checkedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var now = checkedAt.AddMinutes(3);
        var snap = new ServerProbeSnapshot("s1", ServerProbeStatus.Online, null, checkedAt);
        var text = ProbeScheduler.FormatTooltip(snap, now);
        Assert.Contains("Login server reachable", text);
        Assert.Contains("checked 3 min ago", text);
    }

    private static ProbeScheduler CreateScheduler(
        IConnectionProbe probe,
        Func<DateTimeOffset>? clock = null,
        Func<int, int>? jitter = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(probe, clock, delay, jitter);

    private static async Task<ServerProbeSnapshot> WaitForStatusAsync(
        ProbeScheduler scheduler,
        string serverId,
        ServerProbeStatus status,
        Action? start = null,
        TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<ServerProbeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(ServerProbeSnapshot snap)
        {
            if (snap.ServerId == serverId && snap.Status == status)
                tcs.TrySetResult(snap);
        }

        scheduler.StatusChanged += Handler;
        try
        {
            start?.Invoke();
            if (scheduler.TryGetStatus(serverId, out var existing) && existing.Status == status)
                return existing;
            return await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        }
        finally
        {
            scheduler.StatusChanged -= Handler;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(15);
        }

        Assert.True(condition(), "condition not met before timeout");
    }

    private sealed class ControllableDelay
    {
        private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Delay(TimeSpan delay, CancellationToken token)
        {
            if (delay <= TimeSpan.Zero)
                return Task.CompletedTask;
            return _gate.Task.WaitAsync(token);
        }

        public void Release()
        {
            var old = _gate;
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            old.TrySetResult();
        }
    }

    private sealed class FakeClock(DateTimeOffset start)
    {
        private DateTimeOffset _now = start;
        public DateTimeOffset UtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class ScriptedProbe : IConnectionProbe
    {
        private readonly Func<CancellationToken, Task<ConnectionProbeResult>> _handler;

        public ScriptedProbe(Func<CancellationToken, ConnectionProbeResult> handler)
            : this(ct => Task.FromResult(handler(ct)))
        {
        }

        public ScriptedProbe(Func<CancellationToken, Task<ConnectionProbeResult>> handler) => _handler = handler;

        public int Calls { get; private set; }
        public bool SawCompleted { get; private set; }

        public async Task<ConnectionProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            var result = await _handler(cancellationToken).ConfigureAwait(false);
            SawCompleted = true;
            return result;
        }
    }
}
