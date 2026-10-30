using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Instances;

/// <summary>Result of <see cref="SingleInstance.Claim"/>.</summary>
public enum ClaimOutcome
{
    /// <summary>This process holds the lock and (normally) listens for messages. Keep the claim alive until exit.</summary>
    Primary,

    /// <summary>Another instance holds the lock and accepted the message. This process should exit.</summary>
    Forwarded,

    /// <summary>
    /// Another instance holds the lock but did not answer in time (hung, stopped, or without a channel). This process
    /// may start anyway as a secondary instance; data stays safe through the config-directory locks.
    /// </summary>
    PrimaryUnresponsive,

    /// <summary>The lock could not be taken for a reason other than contention (for example a read-only directory).</summary>
    Unavailable,
}

/// <summary>Options for <see cref="SingleInstance.Claim"/>.</summary>
public sealed record SingleInstanceOptions
{
    /// <summary>
    /// Total time a second launch spends handing over its message: connecting (retried, the primary may still be
    /// binding its channel), sending and waiting for the reply. Default 3 s.
    /// </summary>
    public TimeSpan ForwardTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long the primary waits for one client to send its request line. Default 1 s (a real client writes at once
    /// after connecting). Up to <see cref="MaxConcurrentRequests"/> clients are served at the same time, so a few
    /// silent connections cannot use up a second launch's <see cref="ForwardTimeout"/>.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Connections the primary serves concurrently. Default 8.</summary>
    public int MaxConcurrentRequests { get; init; } = 8;

    /// <summary>
    /// Test seam: the user id the peer of a Unix socket must have (Linux, checked with <c>SO_PEERCRED</c> on both sides).
    /// Null means the effective user id of this process.
    /// </summary>
    internal uint? ExpectedPeerUserId { get; init; }

    /// <summary>
    /// Called in the second launch after it connected and before it sends, with the primary's process id when the
    /// platform reports it (Windows named pipes). The app uses it to allow the primary to take the foreground.
    /// </summary>
    public Action<int>? BeforeForward { get; init; }
}

/// <summary>
/// Single-instance guard: the first process to open <see cref="InstanceEndpoint.LockPath"/> exclusively is the primary
/// and listens on <see cref="InstanceEndpoint.ChannelAddress"/>; later launches send an <see cref="InstanceMessage"/>
/// there and exit.
/// </summary>
/// <remarks>
/// The lock is an OS lock on an open file (<c>flock</c> on Unix through <see cref="FileShare.None"/>, a sharing lock on
/// Windows), so the OS drops it when the process dies, however it dies. A leftover lock file or socket file therefore
/// never blocks a start: whoever gets the lock removes the stale socket and binds a new one. File and socket handles are
/// not inherited by child processes (.NET opens them close-on-exec / non-inheritable), so a game started from the app
/// does not keep the lock alive.
/// </remarks>
public static class SingleInstance
{
    /// <summary>
    /// Becomes the primary instance, or hands <paramref name="message"/> to the existing one.
    /// </summary>
    /// <param name="endpoint">Lock and channel location.</param>
    /// <param name="message">What a second launch sends (usually <see cref="InstanceMessage.Focus"/>).</param>
    /// <param name="handler">
    /// In the primary: called on a background thread for every valid request. Must not block; post work to the UI
    /// thread and return. Exceptions are reported to the client as <see cref="InstanceReply.Invalid"/>.
    /// </param>
    public static InstanceClaim Claim(
        InstanceEndpoint endpoint,
        InstanceMessage message,
        Func<InstanceMessage, InstanceReply> handler,
        SingleInstanceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(handler);
        options ??= new SingleInstanceOptions();

        FileStream? lockStream;
        try
        {
            lockStream = TryLock(endpoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new InstanceClaim(ClaimOutcome.Unavailable, null, null, InstanceReply.Invalid, ex.Message);
        }

        if (lockStream is not null)
        {
            InstanceListener? listener = null;
            string? detail = null;
            try
            {
                listener = InstanceListener.Start(endpoint, handler, options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException or NotSupportedException or ArgumentException)
            {
                detail = $"The instance channel could not be opened: {ex.Message}";
            }
            return new InstanceClaim(ClaimOutcome.Primary, lockStream, listener, InstanceReply.Ok, detail);
        }

        var (reply, error) = Forward(endpoint, message, options);
        return reply is null
            ? new InstanceClaim(ClaimOutcome.PrimaryUnresponsive, null, null, InstanceReply.Invalid, error)
            : new InstanceClaim(ClaimOutcome.Forwarded, null, null, reply.Value, null);
    }

    /// <summary>Opens the lock file exclusively without waiting; null when another process holds it.</summary>
    private static FileStream? TryLock(InstanceEndpoint endpoint)
    {
        PrivateStorage.CreatePrivateDirectory(endpoint.Directory);
        try
        {
            var stream = new FileStream(endpoint.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(endpoint.LockPath, PrivateStorage.PrivateFileMode);
            return stream;
        }
        catch (IOException ex) when (ConfigDirectoryLock.IsLockContention(ex))
        {
            return null;
        }
    }

    /// <summary>Sends <paramref name="message"/> to the primary. Returns null and a reason when it could not.</summary>
    internal static (InstanceReply? Reply, string? Error) Forward(InstanceEndpoint endpoint, InstanceMessage message, SingleInstanceOptions options)
    {
        if (endpoint.ChannelAddress is null)
            return (null, "No instance channel is available for this directory.");

        var request = InstanceProtocol.EncodeRequest(message);
        var deadline = DateTime.UtcNow + options.ForwardTimeout;
        string error = "The running instance did not answer in time.";
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return (null, error);

            Stream? stream = null;
            int? peer = null;
            try
            {
                (stream, peer) = Connect(endpoint, remaining, options);
            }
            catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or OperationCanceledException)
            {
                // Not listening yet (the primary takes the lock before it binds) or a stale address: retry.
                error = $"The running instance did not accept a connection: {ex.Message}";
            }
            catch (UnauthorizedAccessException ex)
            {
                // A pipe or socket owned by another user, or a socket folder that is not private: never talk to it.
                return (null, $"Refused the instance channel: {ex.Message}");
            }

            if (stream is null)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(100, Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds))));
                continue;
            }

            using (stream)
            {
                if (peer is { } pid)
                    options.BeforeForward?.Invoke(pid);
                try
                {
                    using var cts = new CancellationTokenSource(ClampTimeout(deadline - DateTime.UtcNow));
                    return (Exchange(stream, request, cts.Token).GetAwaiter().GetResult(), null);
                }
                catch (OperationCanceledException)
                {
                    return (null, "The running instance accepted the connection but did not answer in time.");
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    return (null, $"The running instance closed the connection: {ex.Message}");
                }
            }
        }
    }

    private static async Task<InstanceReply> Exchange(Stream stream, byte[] request, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        var line = await InstanceProtocol.ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
        return line is null ? throw new IOException("No reply.") : InstanceProtocol.DecodeReply(line);
    }

    private static (Stream Stream, int? PeerProcessId) Connect(InstanceEndpoint endpoint, TimeSpan remaining, SingleInstanceOptions options)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", endpoint.ChannelAddress!, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                pipe.Connect((int)Math.Clamp(remaining.TotalMilliseconds, 1, int.MaxValue));
                int? pid = Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id) ? (int)id : null;
                return (pipe, pid);
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }

        // The shared-temp fallback folder must be private before we connect into it.
        endpoint.VerifyPrivateSocketDirectory();
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            // Async with a deadline: a blocking connect could wait on a full backlog of a stopped primary.
            using (var cts = new CancellationTokenSource(ClampTimeout(remaining)))
                socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.ChannelAddress!), cts.Token).AsTask().GetAwaiter().GetResult();
            // Only talk to a primary run by the same user (also closes the race of a folder swapped after the check).
            PeerCredentials.Verify(socket, options.ExpectedPeerUserId);
            return (new NetworkStream(socket, ownsSocket: true), null);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static TimeSpan ClampTimeout(TimeSpan value) => value < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : value;

    private static class Native
    {
        // Windows only; resolved lazily, never called on other platforms.
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    }
}

/// <summary>
/// Held by the primary instance for its whole lifetime (dispose on exit). For other outcomes it holds nothing and
/// only reports what happened.
/// </summary>
public sealed class InstanceClaim : IDisposable
{
    private readonly FileStream? _lock;
    private readonly InstanceListener? _listener;

    internal InstanceClaim(ClaimOutcome outcome, FileStream? lockStream, InstanceListener? listener, InstanceReply reply, string? detail)
    {
        Outcome = outcome;
        _lock = lockStream;
        _listener = listener;
        Reply = reply;
        Detail = detail;
    }

    /// <summary>A claim that does nothing, for launches that opt out (for example developer modes).</summary>
    public static InstanceClaim None { get; } = new(ClaimOutcome.Unavailable, null, null, InstanceReply.Invalid, "Single-instance check skipped.");

    public ClaimOutcome Outcome { get; }

    /// <summary>For <see cref="ClaimOutcome.Forwarded"/>: what the primary answered.</summary>
    public InstanceReply Reply { get; }

    /// <summary>Why the claim did not fully succeed (diagnostics only), or null.</summary>
    public string? Detail { get; }

    /// <summary>True when the primary is listening for messages.</summary>
    public bool IsListening => _listener is not null;

    /// <summary>True when the caller should exit because the running instance took over.</summary>
    public bool ShouldExit => Outcome == ClaimOutcome.Forwarded;

    public void Dispose()
    {
        // Remove the socket while the lock is still held, so no newer primary's socket is ever deleted.
        _listener?.Dispose();
        _lock?.Dispose();
    }
}
