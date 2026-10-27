using System.Collections.Concurrent;
using System.Net.Sockets;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Network;

/// <summary>Status of one server's login-port probe for the UI status dot.</summary>
public enum ServerProbeStatus
{
    /// <summary>Not probed yet, or DNS could not resolve the host.</summary>
    Unknown,

    /// <summary>A probe is in flight.</summary>
    Checking,

    /// <summary>TCP connect to the login port succeeded.</summary>
    Online,

    /// <summary>Connect was refused, timed out, or failed for a non-DNS reason.</summary>
    Offline,
}

/// <summary>Immutable probe state for one server; pushed on <see cref="ProbeScheduler.StatusChanged"/>.</summary>
/// <param name="ServerId">Server id from the store.</param>
/// <param name="Status">Mapped status for the dot.</param>
/// <param name="Reason">Short English reason for tooltips (DNS, refused, timed out, …); null when online or not yet known.</param>
/// <param name="CheckedAtUtc">When the last finished probe completed; null while checking or never probed.</param>
public sealed record ServerProbeSnapshot(
    string ServerId,
    ServerProbeStatus Status,
    string? Reason = null,
    DateTimeOffset? CheckedAtUtc = null);

/// <summary>One server the scheduler may probe: id plus the stored realmlist host (already validated).</summary>
public sealed record ProbeServerTarget(string ServerId, string Host);

/// <summary>
/// Schedules connect-only TCP probes for the Servers list status dot (docs/updates-and-status.md §2).
/// At most one probe every 5 minutes per server (+0–30 s jitter), at most 4 concurrent unfinished ops,
/// single-flight per server. No probes until the list is shown. UI owns the dot; this pushes snapshots.
/// </summary>
public sealed class ProbeScheduler : IDisposable
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxJitter = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    public const int MaxConcurrent = 4;

    public const string ReasonLoginReachable = "Login server reachable";
    public const string ReasonDns = "name not found (DNS)";
    public const string ReasonTimedOut = "timed out";
    public const string ReasonConnectionRefused = "connection refused";

    private readonly IConnectionProbe _probe;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<int, int> _nextJitterMs;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, ServerState> _states = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _concurrency = new(MaxConcurrent, MaxConcurrent);

    private List<ProbeServerTarget> _servers = [];
    private CancellationTokenSource _cts = new();
    private bool _listVisible;
    private bool _enabled = true;
    private bool _disposed;
    private int _unfinishedOps;

    public ProbeScheduler(
        IConnectionProbe probe,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<int, int>? nextJitterMs = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
        _nextJitterMs = nextJitterMs ?? (maxMs => Random.Shared.Next(0, maxMs + 1));
    }

    /// <summary>Raised whenever a server's snapshot changes (checking, result, reset). May run on a thread-pool thread.</summary>
    public event Action<ServerProbeSnapshot>? StatusChanged;

    /// <summary>How many <see cref="IConnectionProbe.ProbeAsync"/> calls have started but not finished.</summary>
    public int UnfinishedOperations
    {
        get
        {
            lock (_gate)
                return _unfinishedOps;
        }
    }

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
                return _enabled;
        }
    }

    public bool IsListVisible
    {
        get
        {
            lock (_gate)
                return _listVisible;
        }
    }

    public bool TryGetStatus(string serverId, out ServerProbeSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        if (_states.TryGetValue(serverId, out var state))
        {
            snapshot = state.ToSnapshot(serverId);
            return true;
        }

        snapshot = new ServerProbeSnapshot(serverId, ServerProbeStatus.Unknown);
        return false;
    }

    /// <summary>Replaces the set of servers that may be probed. Hosts must already be validated realmlist hosts.</summary>
    public void SetServers(IReadOnlyList<ProbeServerTarget> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            var next = servers
                .Where(static s => !string.IsNullOrWhiteSpace(s.ServerId))
                .GroupBy(static s => s.ServerId, StringComparer.Ordinal)
                .Select(static g => g.Last())
                .ToList();
            _servers = next;

            var keep = new HashSet<string>(next.Select(static s => s.ServerId), StringComparer.Ordinal);
            foreach (var id in _states.Keys.ToArray())
            {
                if (!keep.Contains(id))
                    _states.TryRemove(id, out _);
            }

            foreach (var target in next)
            {
                var state = _states.GetOrAdd(target.ServerId, static _ => new ServerState());
                if (state.TrackedHost is not null
                    && !string.Equals(state.TrackedHost, target.Host, StringComparison.OrdinalIgnoreCase))
                {
                    state.ResetToUnknown();
                }

                state.TrackedHost = target.Host;
            }
        }

        KickScheduling();
    }

    /// <summary>
    /// Marks whether the Servers list is shown (not minimized). Probes never run at startup until this is true.
    /// Hiding the list does not clear results; showing it again reuses results younger than 5 minutes.
    /// </summary>
    public void NotifyListVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _listVisible = visible;
        }

        KickScheduling();
    }

    /// <summary>
    /// Mirrors <c>status.probe_enabled</c>. When false, cancels in-flight probes and resets snapshots to Unknown
    /// (UI hides the dot). When true again, scheduling resumes if the list is visible.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancellationTokenSource? oldCts = null;
        List<(string Id, ServerProbeSnapshot Snap)> resets = [];
        lock (_gate)
        {
            if (_enabled == enabled)
                return;
            _enabled = enabled;
            if (!enabled)
            {
                oldCts = _cts;
                _cts = new CancellationTokenSource();
                foreach (var pair in _states)
                {
                    pair.Value.CancelInFlight();
                    pair.Value.ResetToUnknown();
                    pair.Value.InvalidateLoop();
                    resets.Add((pair.Key, pair.Value.ToSnapshot(pair.Key)));
                }
            }
        }

        foreach (var (_, snap) in resets)
            Raise(snap);

        try
        {
            oldCts?.Cancel();
        }
        finally
        {
            oldCts?.Dispose();
        }

        if (enabled)
            KickScheduling();
    }

    /// <summary>Builds the status-dot tooltip text from a snapshot (English; UI may localize later).</summary>
    public static string FormatTooltip(ServerProbeSnapshot snapshot, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Status switch
        {
            ServerProbeStatus.Online => AppendAge(ReasonLoginReachable, snapshot.CheckedAtUtc, nowUtc),
            ServerProbeStatus.Offline => AppendAge(snapshot.Reason ?? "offline", snapshot.CheckedAtUtc, nowUtc),
            ServerProbeStatus.Unknown when !string.IsNullOrEmpty(snapshot.Reason) =>
                AppendAge(snapshot.Reason, snapshot.CheckedAtUtc, nowUtc),
            ServerProbeStatus.Checking => "Checking login server…",
            _ => "Login server status unknown",
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CancellationTokenSource cts;
        lock (_gate)
        {
            _enabled = false;
            _listVisible = false;
            cts = _cts;
            foreach (var state in _states.Values)
            {
                state.CancelInFlight();
                state.InvalidateLoop();
            }
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
        _concurrency.Dispose();
    }

    private void KickScheduling()
    {
        List<ProbeServerTarget> servers;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || !_enabled || !_listVisible)
                return;
            servers = _servers.ToList();
            token = _cts.Token;
        }

        foreach (var target in servers)
        {
            if (!_states.TryGetValue(target.ServerId, out var state))
                continue;
            if (state.TryClaimLoop())
                _ = ExceptionSafeRun(() => RunServerLoopAsync(target, state, token));
        }
    }

    private async Task RunServerLoopAsync(ProbeServerTarget target, ServerState state, CancellationToken appToken)
    {
        var serverId = target.ServerId;
        var loopGen = state.LoopGeneration;
        try
        {
            while (!appToken.IsCancellationRequested && !_disposed)
            {
                TimeSpan wait;
                ProbeServerTarget? current;
                lock (_gate)
                {
                    if (!_enabled || !_listVisible || _disposed)
                        return;
                    if (!state.OwnsLoop(loopGen))
                        return;
                    var match = _servers.FirstOrDefault(s => s.ServerId == serverId);
                    if (match is null)
                        return;
                    current = match;
                    if (state.InFlight)
                        return;

                    var now = _clock();
                    if (state.LastStartedUtc is { } last)
                    {
                        var elapsed = now - last;
                        if (elapsed < MinInterval)
                        {
                            var jitter = TimeSpan.FromMilliseconds(_nextJitterMs((int)MaxJitter.TotalMilliseconds));
                            wait = (MinInterval - elapsed) + jitter;
                        }
                        else
                        {
                            // Already past the floor: jitter only adds delay.
                            wait = TimeSpan.FromMilliseconds(_nextJitterMs((int)MaxJitter.TotalMilliseconds));
                        }
                    }
                    else
                    {
                        wait = TimeSpan.Zero;
                    }
                }

                if (wait > TimeSpan.Zero)
                {
                    try
                    {
                        await _delay(wait, appToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (appToken.IsCancellationRequested)
                    {
                        return;
                    }

                    continue;
                }

                await ProbeOnceAsync(current, state, appToken).ConfigureAwait(false);
            }
        }
        finally
        {
            state.ReleaseLoop(loopGen);
            // If we exited because a delay finished and conditions flipped back on, another Kick may be needed.
            KickScheduling();
        }
    }

    private async Task ProbeOnceAsync(ProbeServerTarget target, ServerState state, CancellationToken appToken)
    {
        CancellationTokenSource linked;
        lock (_gate)
        {
            if (!_enabled || !_listVisible || _disposed)
                return;
            if (state.InFlight)
                return;

            var now = _clock();
            // List reopen must not bypass the 5-minute minimum.
            if (state.LastStartedUtc is { } last && now - last < MinInterval)
                return;

            linked = CancellationTokenSource.CreateLinkedTokenSource(appToken);
            state.BeginFlight(linked, now);
        }

        Raise(new ServerProbeSnapshot(target.ServerId, ServerProbeStatus.Checking, state.Reason, state.CheckedAtUtc));

        var acquired = false;
        try
        {
            await _concurrency.WaitAsync(linked.Token).ConfigureAwait(false);
            acquired = true;

            if (!RealmlistAddress.TryParseHostAddress(target.Host, out var host, out var port))
            {
                Finish(linked, target.ServerId, state, ServerProbeStatus.Unknown, "invalid realmlist host", _clock());
                return;
            }

            host = StripIpv6Brackets(host);

            lock (_gate)
                _unfinishedOps++;

            ConnectionProbeResult result;
            try
            {
                result = await _probe.ProbeAsync(host, port, ProbeTimeout, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                lock (_gate)
                {
                    if (_unfinishedOps > 0)
                        _unfinishedOps--;
                }
            }

            if (linked.IsCancellationRequested)
                return;

            var checkedAt = _clock();
            MapResult(result, out var status, out var reason);
            Finish(linked, target.ServerId, state, status, reason, checkedAt);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // Disabled/disposed: SetEnabled already reset snapshots.
        }
        finally
        {
            if (acquired)
            {
                try
                {
                    _concurrency.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            lock (_gate)
                state.EndFlight(linked);

            linked.Dispose();
        }
    }

    private void Finish(
        CancellationTokenSource flight,
        string serverId,
        ServerState state,
        ServerProbeStatus status,
        string? reason,
        DateTimeOffset checkedAt)
    {
        ServerProbeSnapshot snap;
        lock (_gate)
        {
            if (flight.IsCancellationRequested || !state.OwnsFlight(flight))
                return;
            if (!_states.TryGetValue(serverId, out var current) || !ReferenceEquals(current, state))
                return;

            state.ApplyResult(status, reason, checkedAt);
            snap = state.ToSnapshot(serverId);
        }

        Raise(snap);
    }

    private void Raise(ServerProbeSnapshot snapshot)
    {
        try
        {
            StatusChanged?.Invoke(snapshot);
        }
        catch
        {
            // UI handlers must not break the scheduler.
        }
    }

    private static async Task ExceptionSafeRun(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    internal static void MapResult(ConnectionProbeResult result, out ServerProbeStatus status, out string? reason)
    {
        switch (result.Outcome)
        {
            case ProbeResult.Ok:
                status = ServerProbeStatus.Online;
                reason = null;
                return;
            case ProbeResult.TimedOut:
                status = ServerProbeStatus.Offline;
                reason = ReasonTimedOut;
                return;
            default:
                if (result.SocketError is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
                {
                    status = ServerProbeStatus.Unknown;
                    reason = ReasonDns;
                    return;
                }

                status = ServerProbeStatus.Offline;
                reason = FormatSocketReason(result.SocketError);
                return;
        }
    }

    private static string FormatSocketReason(SocketError? error) =>
        error switch
        {
            null => "network error",
            SocketError.ConnectionRefused => ReasonConnectionRefused,
            SocketError.TimedOut => ReasonTimedOut,
            SocketError.NetworkUnreachable => $"network error ({error})",
            SocketError.HostUnreachable => $"network error ({error})",
            SocketError.NetworkDown => $"network error ({error})",
            _ => $"network error ({error})",
        };

    private static string StripIpv6Brackets(string host)
    {
        if (host.Length >= 2 && host[0] == '[' && host[^1] == ']')
            return host[1..^1];
        return host;
    }

    private static string AppendAge(string head, DateTimeOffset? checkedAt, DateTimeOffset now)
    {
        if (checkedAt is null)
            return head;
        var age = now - checkedAt.Value;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;
        string ageText;
        if (age < TimeSpan.FromMinutes(1))
            ageText = "checked just now";
        else
        {
            var minutes = (int)age.TotalMinutes;
            ageText = minutes == 1 ? "checked 1 min ago" : $"checked {minutes} min ago";
        }

        return $"{head} ({ageText})";
    }

    private sealed class ServerState
    {
        private CancellationTokenSource? _flightCts;
        private int _loopRunning;
        private int _loopGeneration;

        public int LoopGeneration => Volatile.Read(ref _loopGeneration);

        public bool LoopRunning
        {
            get => Volatile.Read(ref _loopRunning) != 0;
            set => Volatile.Write(ref _loopRunning, value ? 1 : 0);
        }

        public bool TryClaimLoop()
        {
            if (Interlocked.CompareExchange(ref _loopRunning, 1, 0) != 0)
                return false;
            Interlocked.Increment(ref _loopGeneration);
            return true;
        }

        public bool OwnsLoop(int generation) =>
            Volatile.Read(ref _loopRunning) != 0 && Volatile.Read(ref _loopGeneration) == generation;

        public void ReleaseLoop(int generation)
        {
            if (Volatile.Read(ref _loopGeneration) != generation)
                return;
            Volatile.Write(ref _loopRunning, 0);
        }

        public string? TrackedHost { get; set; }

        public bool InFlight { get; private set; }
        public DateTimeOffset? LastStartedUtc { get; private set; }
        public ServerProbeStatus Status { get; private set; } = ServerProbeStatus.Unknown;
        public string? Reason { get; private set; }
        public DateTimeOffset? CheckedAtUtc { get; private set; }

        public void BeginFlight(CancellationTokenSource cts, DateTimeOffset now)
        {
            CancelInFlight();
            _flightCts = cts;
            InFlight = true;
            LastStartedUtc = now;
            Status = ServerProbeStatus.Checking;
        }

        public bool OwnsFlight(CancellationTokenSource cts) => ReferenceEquals(_flightCts, cts);

        public void EndFlight(CancellationTokenSource cts)
        {
            if (!ReferenceEquals(_flightCts, cts))
                return;
            InFlight = false;
            _flightCts = null;
        }

        public void CancelInFlight()
        {
            try
            {
                _flightCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            InFlight = false;
            _flightCts = null;
        }

        public void ApplyResult(ServerProbeStatus status, string? reason, DateTimeOffset checkedAt)
        {
            Status = status;
            Reason = reason;
            CheckedAtUtc = checkedAt;
        }

        public void InvalidateLoop()
        {
            Interlocked.Increment(ref _loopGeneration);
            Volatile.Write(ref _loopRunning, 0);
        }

        public void ResetToUnknown()
        {
            Status = ServerProbeStatus.Unknown;
            Reason = null;
            CheckedAtUtc = null;
            LastStartedUtc = null;
        }

        public ServerProbeSnapshot ToSnapshot(string serverId) =>
            new(serverId, Status, Reason, CheckedAtUtc);
    }
}
