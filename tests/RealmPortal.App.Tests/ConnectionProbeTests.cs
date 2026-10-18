using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using RealmPortal.App.Portal;

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

        Assert.Equal(ProbeResult.TimedOut, result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"timed out in {watch.Elapsed}");
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

        Assert.Equal(ProbeResult.TimedOut, result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"timed out in {watch.Elapsed}");
    }

    [Fact]
    public async Task DnsFailure_IsFailed()
    {
        var probe = new TcpConnectionProbe((_, _) => throw new SocketException((int)SocketError.HostNotFound));
        var result = await probe.ProbeAsync("missing.invalid", 3724, TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(ProbeResult.Failed, result);
    }

    [Fact]
    public async Task NoAddresses_IsFailed()
    {
        var probe = new TcpConnectionProbe((_, _) => Task.FromResult(Array.Empty<IPAddress>()));
        var result = await probe.ProbeAsync("empty.invalid", 3724, TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(ProbeResult.Failed, result);
    }

    [Fact]
    public async Task UnresolvableInvalidHost_IsNeverOk()
    {
        var probe = new TcpConnectionProbe();
        var result = await probe.ProbeAsync("nonexistent.invalid", 3724, TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.NotEqual(ProbeResult.Ok, result);
        Assert.True(result is ProbeResult.Failed or ProbeResult.TimedOut);
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
    public async Task TriesEachAddressUntilOneConnects()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            // Nothing is listening on IPv6 loopback for this port, so that attempt fails and IPv4 must be tried.
            var probe = new TcpConnectionProbe((_, _) =>
                Task.FromResult(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }));
            var result = await probe.ProbeAsync("loopback.invalid", port, TimeSpan.FromSeconds(3), CancellationToken.None);
            Assert.Equal(ProbeResult.Ok, result);
        }
        finally
        {
            listener.Stop();
        }
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

            var result = await probe.ProbeAsync("late.invalid", port, TimeSpan.FromMilliseconds(200), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(ProbeResult.TimedOut, result);
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
