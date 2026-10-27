using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using RealmPortal.Core.Network;

namespace RealmPortal.App.Tests;

public class ConnectionProbeTests
{
    [Fact]
    public async Task SlowResolverThatIgnoresCancellation_TimesOutWithinTheBudget()
    {
        var budget = TimeSpan.FromMilliseconds(300);
        var probe = new TcpConnectionProbe(async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            return new[] { IPAddress.Loopback };
        });

        var watch = Stopwatch.StartNew();
        var result = await probe.ProbeAsync("slow.invalid", 9, budget, CancellationToken.None);
        watch.Stop();

        Assert.Equal(ProbeResult.TimedOut, result.Outcome);
        Assert.True(watch.Elapsed < budget + TimeSpan.FromSeconds(10), $"timed out in {watch.Elapsed}");
    }

    [Fact]
    public async Task ResolverThatHonorsCancellation_TimesOutWithinTheBudget()
    {
        var budget = TimeSpan.FromMilliseconds(300);
        var probe = new TcpConnectionProbe(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
            return new[] { IPAddress.Loopback };
        });

        var watch = Stopwatch.StartNew();
        var result = await probe.ProbeAsync("slow.invalid", 9, budget, CancellationToken.None);
        watch.Stop();

        Assert.Equal(ProbeResult.TimedOut, result.Outcome);
        Assert.True(watch.Elapsed < budget + TimeSpan.FromSeconds(10), $"timed out in {watch.Elapsed}");
    }

    [Fact]
    public async Task DnsHostNotFound_IsFailedWithSocketError()
    {
        var probe = new TcpConnectionProbe((_, _) => throw new SocketException((int)SocketError.HostNotFound));
        var result = await probe.ProbeAsync("missing.invalid", 3724, TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(ProbeResult.Failed, result.Outcome);
        Assert.Equal(SocketError.HostNotFound, result.SocketError);
    }

    [Fact]
    public async Task NoAddresses_IsFailedWithNoData()
    {
        var probe = new TcpConnectionProbe((_, _) => Task.FromResult(Array.Empty<IPAddress>()));
        var result = await probe.ProbeAsync("empty.invalid", 3724, TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(ProbeResult.Failed, result.Outcome);
        Assert.Equal(SocketError.NoData, result.SocketError);
    }

    [Fact]
    public async Task UnresolvableInvalidHost_IsNeverOk()
    {
        // Inject HostNotFound — ambient DNS may NXDOMAIN-hijack *.invalid on some networks.
        var probe = new TcpConnectionProbe((_, _) =>
            throw new SocketException((int)SocketError.HostNotFound));
        var result = await probe.ProbeAsync("nonexistent.invalid", 3724, TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.NotEqual(ProbeResult.Ok, result.Outcome);
        Assert.True(result.Outcome is ProbeResult.Failed or ProbeResult.TimedOut);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        var probe = new TcpConnectionProbe(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return new[] { IPAddress.Loopback };
        });
        using var cts = new CancellationTokenSource();
        var pending = probe.ProbeAsync("blocked.invalid", 9, TimeSpan.FromSeconds(5), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task LoopbackListener_IsOnline()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probe = new TcpConnectionProbe((_, _) =>
                Task.FromResult(new[] { IPAddress.Loopback }));
            var result = await probe.ProbeAsync("loopback.invalid", port, TimeSpan.FromSeconds(3), CancellationToken.None);
            Assert.Equal(ProbeResult.Ok, result.Outcome);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ClosedPort_IsFailed()
    {
        // Bind then stop so the port is unlikely to accept connections.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var probe = new TcpConnectionProbe((_, _) => Task.FromResult(new[] { IPAddress.Loopback }));
        var result = await probe.ProbeAsync("closed.invalid", port, TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(ProbeResult.Failed, result.Outcome);
        Assert.NotNull(result.SocketError);
    }

    [Fact]
    public async Task TriesEachAddressUntilOneConnects()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probe = new TcpConnectionProbe((_, _) =>
                Task.FromResult(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }));
            var result = await probe.ProbeAsync("loopback.invalid", port, TimeSpan.FromSeconds(3), CancellationToken.None);
            Assert.Equal(ProbeResult.Ok, result.Outcome);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task InjectedConnectTimeout_IsTimedOut()
    {
        var probe = new TcpConnectionProbe(
            resolve: (_, _) => Task.FromResult(new[] { IPAddress.Loopback }),
            connect: async (_, _, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            });

        var result = await probe.ProbeAsync("inject.invalid", 9, TimeSpan.FromMilliseconds(200), CancellationToken.None);
        Assert.Equal(ProbeResult.TimedOut, result.Outcome);
    }

    [Fact]
    public async Task LateResolverAfterTimeout_DoesNotConnect()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken? resolverToken = null;
            var probe = new TcpConnectionProbe(async (_, token) =>
            {
                resolverToken = token;
                await release.Task.ConfigureAwait(false);
                return new[] { IPAddress.Loopback };
            });

            var budget = TimeSpan.FromMilliseconds(200);
            var result = await probe.ProbeAsync("late.invalid", port, budget, CancellationToken.None)
                .WaitAsync(budget + TimeSpan.FromSeconds(10));

            Assert.Equal(ProbeResult.TimedOut, result.Outcome);
            Assert.True(resolverToken?.IsCancellationRequested);

            release.TrySetResult();
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromMilliseconds(500))
            {
                Assert.False(listener.Pending());
                await Task.Delay(25);
            }

            Assert.False(listener.Pending());
            Assert.True(resolverToken?.IsCancellationRequested);
        }
        finally
        {
            listener.Stop();
        }
    }
}
