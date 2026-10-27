using System.Net;
using System.Net.Sockets;
using RealmPortal.App.Network;
using RealmPortal.App.Portal;

namespace RealmPortal.App.Tests;

public class ProbeSchedulerTests
{
    private sealed class FakeProbe : IConnectionProbe
    {
        public ProbeResult Result { get; set; } = ProbeResult.Ok;
        public int Calls;

        public Task<ProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Result);
        }
    }

    private static ProbeScheduler Create(
        FakeProbe probe,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null) =>
        new(
            probe,
            resolve: resolve ?? ((_, _) => Task.FromResult(new[] { IPAddress.Loopback })),
            minInterval: TimeSpan.FromMilliseconds(50),
            jitterMax: TimeSpan.Zero,
            connectTimeout: TimeSpan.FromMilliseconds(200));

    [Fact]
    public async Task NoProbesUntilListVisible()
    {
        var probe = new FakeProbe();
        using var scheduler = Create(probe);
        var seen = new List<ServerProbeSnapshot>();
        scheduler.StatusChanged += seen.Add;

        scheduler.SetEnabled(true);
        scheduler.SetServers([("a", "127.0.0.1", 3724)]);
        await Task.Delay(150);
        Assert.Equal(0, probe.Calls);
        Assert.Empty(seen);

        scheduler.NotifyListVisible(true);
        await WaitForAsync(() => probe.Calls >= 1, TimeSpan.FromSeconds(2));
        Assert.True(probe.Calls >= 1);
    }

    [Fact]
    public async Task Online_PublishesGreenState()
    {
        var probe = new FakeProbe { Result = ProbeResult.Ok };
        using var scheduler = Create(probe);
        ServerProbeSnapshot? last = null;
        scheduler.StatusChanged += s => last = s;
        scheduler.SetServers([("s1", "127.0.0.1", 3724)]);
        scheduler.NotifyListVisible(true);

        await WaitForAsync(() => last?.Status == LoginReachability.Online, TimeSpan.FromSeconds(2));
        Assert.Equal(LoginReachability.Online, last!.Status);
        Assert.True(scheduler.TryGetStatus("s1", out var snap));
        Assert.Equal(LoginReachability.Online, snap.Status);
        Assert.NotNull(snap.CheckedAtUtc);
    }

    [Fact]
    public async Task TimedOut_IsOfflineWithReason()
    {
        var probe = new FakeProbe { Result = ProbeResult.TimedOut };
        using var scheduler = Create(probe);
        ServerProbeSnapshot? last = null;
        scheduler.StatusChanged += s =>
        {
            if (s.Status != LoginReachability.Checking)
                last = s;
        };
        scheduler.SetServers([("s1", "127.0.0.1", 9)]);
        scheduler.NotifyListVisible(true);

        await WaitForAsync(() => last?.Status == LoginReachability.Offline, TimeSpan.FromSeconds(2));
        Assert.Equal(LoginReachability.Offline, last!.Status);
        Assert.Contains("timed out", last.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DnsFailure_IsUnknownNotOffline()
    {
        var probe = new FakeProbe();
        using var scheduler = Create(
            probe,
            resolve: (_, _) => throw new SocketException((int)SocketError.HostNotFound));
        ServerProbeSnapshot? last = null;
        scheduler.StatusChanged += s =>
        {
            if (s.Status != LoginReachability.Checking)
                last = s;
        };
        scheduler.SetServers([("s1", "missing.invalid", 3724)]);
        scheduler.NotifyListVisible(true);

        await WaitForAsync(() => last?.Status == LoginReachability.Unknown, TimeSpan.FromSeconds(2));
        Assert.Equal(LoginReachability.Unknown, last!.Status);
        Assert.NotNull(last.Reason);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task SetEnabledFalse_PublishesUnknownAndStopsProbing()
    {
        var probe = new FakeProbe();
        using var scheduler = Create(probe);
        scheduler.SetServers([("s1", "127.0.0.1", 3724)]);
        scheduler.NotifyListVisible(true);
        await WaitForAsync(() => probe.Calls >= 1, TimeSpan.FromSeconds(2));
        var callsAfterOnline = probe.Calls;

        var disabled = new List<ServerProbeSnapshot>();
        scheduler.StatusChanged += disabled.Add;
        scheduler.SetEnabled(false);
        Assert.Contains(disabled, s => s.Status == LoginReachability.Unknown && s.Reason == "Probes disabled");

        await Task.Delay(200);
        Assert.Equal(callsAfterOnline, probe.Calls);
    }

    [Fact]
    public async Task MinInterval_PreventsImmediateReprobe()
    {
        var probe = new FakeProbe();
        using var scheduler = new ProbeScheduler(
            probe,
            resolve: (_, _) => Task.FromResult(new[] { IPAddress.Loopback }),
            minInterval: TimeSpan.FromSeconds(30),
            jitterMax: TimeSpan.Zero,
            connectTimeout: TimeSpan.FromMilliseconds(200));
        scheduler.SetServers([("s1", "127.0.0.1", 3724)]);
        scheduler.NotifyListVisible(true);
        await WaitForAsync(() => probe.Calls >= 1, TimeSpan.FromSeconds(2));
        var first = probe.Calls;
        await Task.Delay(150);
        Assert.Equal(first, probe.Calls);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }
        Assert.Fail($"Condition not met within {timeout}");
    }
}
