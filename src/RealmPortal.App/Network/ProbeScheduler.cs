using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using RealmPortal.App.Portal;

namespace RealmPortal.App.Network;

/// <summary>
/// App-layer probe scheduler matching docs/updates-and-status.md until Core's ProbeScheduler lands.
/// Max 4 concurrent; ≥5 min + jitter; no probes until the list is visible; SetEnabled ↔ status.probe_enabled.
/// </summary>
public sealed class ProbeScheduler : IProbeScheduler
{
    public const int MaxConcurrent = 4;
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan JitterMax = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly IConnectionProbe _probe;
    private readonly TimeProvider _time;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly TimeSpan _minInterval;
    private readonly TimeSpan _jitterMax;
    private readonly TimeSpan _connectTimeout;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, ServerProbeSnapshot> _status = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAttempt = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _slots = new(MaxConcurrent, MaxConcurrent);
    private readonly Random _jitter = new();

    private IReadOnlyList<(string ServerId, string Host, int Port)> _servers =
        Array.Empty<(string, string, int)>();
    private bool _listVisible;
    private bool _enabled = true;
    private bool _disposed;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public ProbeScheduler(
        IConnectionProbe? probe = null,
        TimeProvider? time = null,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        TimeSpan? minInterval = null,
        TimeSpan? jitterMax = null,
        TimeSpan? connectTimeout = null)
    {
        _probe = probe ?? new TcpConnectionProbe();
        _time = time ?? TimeProvider.System;
        _resolve = resolve ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
        _minInterval = minInterval ?? MinInterval;
        _jitterMax = jitterMax ?? JitterMax;
        _connectTimeout = connectTimeout ?? ConnectTimeout;
    }

    public event Action<ServerProbeSnapshot>? StatusChanged;

    public bool TryGetStatus(string serverId, out ServerProbeSnapshot snapshot)
        => _status.TryGetValue(serverId, out snapshot!);

    public void SetServers(IReadOnlyList<(string ServerId, string Host, int Port)> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        lock (_gate)
        {
            ThrowIfDisposed();
            _servers = servers.ToArray();
            var keep = new HashSet<string>(servers.Select(s => s.ServerId), StringComparer.Ordinal);
            foreach (var key in _status.Keys)
            {
                if (!keep.Contains(key))
                    _status.TryRemove(key, out _);
            }
            foreach (var key in _lastAttempt.Keys)
            {
                if (!keep.Contains(key))
                    _lastAttempt.TryRemove(key, out _);
            }
        }
        EnsureLoop();
    }

    public void NotifyListVisible(bool visible)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _listVisible = visible;
        }
        EnsureLoop();
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _enabled = enabled;
            if (!enabled)
            {
                foreach (var id in _status.Keys.ToArray())
                    Publish(new ServerProbeSnapshot(id, LoginReachability.Unknown, "Probes disabled", null));
            }
        }
        EnsureLoop();
    }

    private void EnsureLoop()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            var shouldRun = _enabled && _listVisible && _servers.Count > 0;
            if (shouldRun && _loopTask is null)
            {
                _loopCts = new CancellationTokenSource();
                var token = _loopCts.Token;
                _loopTask = Task.Run(() => LoopAsync(token), token);
            }
            else if (!shouldRun && _loopCts is not null)
            {
                _loopCts.Cancel();
                _loopCts.Dispose();
                _loopCts = null;
                _loopTask = null;
            }
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<(string ServerId, string Host, int Port)> snapshot;
            lock (_gate)
            {
                if (!_enabled || !_listVisible || _servers.Count == 0)
                    return;
                snapshot = _servers;
            }

            var now = _time.GetUtcNow();
            foreach (var server in snapshot)
            {
                if (ct.IsCancellationRequested)
                    return;
                if (_lastAttempt.TryGetValue(server.ServerId, out var last))
                {
                    var due = last + _minInterval + TimeSpan.FromMilliseconds(_jitter.Next(Math.Max(1, (int)_jitterMax.TotalMilliseconds)));
                    if (now < due)
                        continue;
                }

                if (!await _slots.WaitAsync(0, ct).ConfigureAwait(false))
                    continue;

                _ = ProbeOneAsync(server, ct);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ProbeOneAsync((string ServerId, string Host, int Port) server, CancellationToken ct)
    {
        try
        {
            Publish(new ServerProbeSnapshot(server.ServerId, LoginReachability.Checking, null, null));
            _lastAttempt[server.ServerId] = _time.GetUtcNow();

            IPAddress[] addresses;
            try
            {
                addresses = await _resolve(server.Host, ct).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                Publish(new ServerProbeSnapshot(
                    server.ServerId,
                    LoginReachability.Unknown,
                    "Host could not be resolved",
                    _time.GetUtcNow()));
                return;
            }
            catch (ArgumentException)
            {
                Publish(new ServerProbeSnapshot(
                    server.ServerId,
                    LoginReachability.Unknown,
                    "Invalid host",
                    _time.GetUtcNow()));
                return;
            }

            if (addresses.Length == 0)
            {
                Publish(new ServerProbeSnapshot(
                    server.ServerId,
                    LoginReachability.Unknown,
                    "Host could not be resolved",
                    _time.GetUtcNow()));
                return;
            }

            var result = await _probe.ProbeAsync(server.Host, server.Port, _connectTimeout, ct).ConfigureAwait(false);
            var checkedAt = _time.GetUtcNow();
            switch (result)
            {
                case ProbeResult.Ok:
                    Publish(new ServerProbeSnapshot(server.ServerId, LoginReachability.Online, null, checkedAt));
                    break;
                case ProbeResult.TimedOut:
                    Publish(new ServerProbeSnapshot(
                        server.ServerId, LoginReachability.Offline, "Connection timed out", checkedAt));
                    break;
                default:
                    Publish(new ServerProbeSnapshot(
                        server.ServerId, LoginReachability.Offline, "Connection failed", checkedAt));
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception)
        {
            Publish(new ServerProbeSnapshot(
                server.ServerId,
                LoginReachability.Offline,
                "Connection failed",
                _time.GetUtcNow()));
        }
        finally
        {
            _slots.Release();
        }
    }

    private void Publish(ServerProbeSnapshot snapshot)
    {
        _status[snapshot.ServerId] = snapshot;
        try
        {
            StatusChanged?.Invoke(snapshot);
        }
        catch
        {
            // UI subscribers must not break the scheduler
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProbeScheduler));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _loopCts = null;
            _loopTask = null;
        }
        _slots.Dispose();
    }
}
