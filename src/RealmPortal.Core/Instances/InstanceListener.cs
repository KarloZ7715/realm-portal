using System.IO.Pipes;
using System.Net.Sockets;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.Instances;

/// <summary>
/// The primary instance's side of the channel: accepts connections and serves up to
/// <see cref="SingleInstanceOptions.MaxConcurrentRequests"/> at once. Each one gets one request line (bounded in size
/// and by <see cref="SingleInstanceOptions.RequestTimeout"/>), one handler call and one reply (kept available for
/// <see cref="SingleInstanceOptions.ReplyTimeout"/>). Garbage, oversized or slow requests get <see cref="InstanceReply.Invalid"/>
/// (when possible) and never stop the listener; silent clients cannot block a real one.
/// </summary>
internal sealed class InstanceListener : IDisposable
{
    private readonly InstanceEndpoint _endpoint;
    private readonly Func<InstanceMessage, InstanceReply> _handler;
    private readonly SingleInstanceOptions _options;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots;
    private readonly Socket? _socket;
    private readonly Task _loop;
    private int _disposed;

    private InstanceListener(InstanceEndpoint endpoint, Func<InstanceMessage, InstanceReply> handler, SingleInstanceOptions options, Socket? socket)
    {
        _endpoint = endpoint;
        _handler = handler;
        _options = options;
        _slots = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentRequests));
        _socket = socket;
        _loop = Task.Run(socket is null ? PipeLoopAsync : SocketLoopAsync);
    }

    /// <summary>Starts listening. Must only be called while holding the instance lock.</summary>
    public static InstanceListener Start(InstanceEndpoint endpoint, Func<InstanceMessage, InstanceReply> handler, SingleInstanceOptions options)
    {
        if (endpoint.ChannelAddress is null)
            throw new IOException("No instance channel is available for this directory.");
        if (OperatingSystem.IsWindows())
            return new InstanceListener(endpoint, handler, options, null);

        endpoint.EnsureSocketDirectory();
        var path = endpoint.ChannelAddress;
        RemoveStaleEntry(path);

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(path));
            // Only the owner may connect (the directory is private as well).
            File.SetUnixFileMode(path, PrivateStorage.PrivateFileMode);
            socket.Listen(16);
            return new InstanceListener(endpoint, handler, options, socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// We hold the lock, so whatever is at the socket path belongs to a process that is gone (or was put there by
    /// hand). A socket, regular file or symlink is unlinked (a symlink is removed itself, never its target). An empty
    /// directory is removed; a non-empty directory is left alone and the bind fails, so the app runs without the
    /// channel (second launches then open a second window) until the directory is removed.
    /// </summary>
    internal static void RemoveStaleEntry(string path)
    {
        var entry = new FileInfo(path);
        if (entry.LinkTarget is not null)
        {
            File.Delete(path); // unlink(2): removes the link, not what it points to
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: false); // throws IOException when not empty
        }
        else if (entry.Exists)
        {
            File.Delete(path);
        }
    }

    private async Task SocketLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            if (!await WaitForSlotAsync().ConfigureAwait(false))
                return;

            Socket client;
            try
            {
                client = await _socket!.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                _slots.Release();
                return;
            }
            catch (SocketException)
            {
                _slots.Release();
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await using var stream = new NetworkStream(client, ownsSocket: true);
                    if (IsSameUser(client))
                        await ServeAsync(stream).ConfigureAwait(false);
                }
                finally
                {
                    _slots.Release();
                }
            });
        }
    }

    private bool IsSameUser(Socket client)
    {
        try
        {
            PeerCredentials.Verify(client, _options.ExpectedPeerUserId);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false; // another user: close without a reply
        }
    }

    private async Task PipeLoopAsync()
    {
        var first = true;
        var instances = Math.Max(1, _options.MaxConcurrentRequests);
        while (!_stop.IsCancellationRequested)
        {
            if (!await WaitForSlotAsync().ConfigureAwait(false))
                return;

            NamedPipeServerStream pipe;
            try
            {
                var options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
                pipe = new NamedPipeServerStream(_endpoint.ChannelAddress!, PipeDirection.InOut, instances, PipeTransmissionMode.Byte, options);
                first = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _slots.Release();
                // The name is taken (another user or a dying instance): retry later rather than spin.
                try
                {
                    await Task.Delay(500, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                _slots.Release();
                if (ex is IOException)
                    continue;
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await using (pipe)
                        await ServeAsync(pipe).ConfigureAwait(false);
                }
                finally
                {
                    _slots.Release();
                }
            });
        }
    }

    private async Task<bool> WaitForSlotAsync()
    {
        try
        {
            await _slots.WaitAsync(_stop.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Handles one connection. Never throws.</summary>
    private async Task ServeAsync(Stream stream)
    {
        try
        {
            byte[]? line;
            using (var request = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                request.CancelAfter(SingleInstance.ClampTimeout(_options.RequestTimeout));
                line = await InstanceProtocol.ReadLineAsync(stream, request.Token).ConfigureAwait(false);
            }

            var message = line is null ? null : InstanceProtocol.TryDecodeRequest(line);
            var reply = message is null ? InstanceReply.Invalid : Handle(message);

            // The reply gets its own deadline: the client may be delayed between sending and reading (a busy
            // machine), and the request deadline may already be close to running out.
            using var replyTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            replyTimeout.CancelAfter(SingleInstance.ClampTimeout(_options.ReplyTimeout));
            await stream.WriteAsync(InstanceProtocol.EncodeReply(reply), replyTimeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(replyTimeout.Token).ConfigureAwait(false);
            if (stream is PipeStream)
            {
                // Closing a Windows pipe handle drops a reply the client has not read yet, so wait (bounded by the
                // reply deadline) until the client closes its end; anything else it sends is ignored.
                var sink = new byte[512];
                while (await stream.ReadAsync(sink, replyTimeout.Token).ConfigureAwait(false) > 0)
                {
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            // A slow, vanished or misbehaving client: drop it and serve the next one.
        }
    }

    private InstanceReply Handle(InstanceMessage message)
    {
        try
        {
            return _handler(message);
        }
        catch (Exception)
        {
            // A failing handler must not take the listener down; the client is told nothing was done.
            return InstanceReply.Invalid;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        _socket?.Dispose();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        if (_socket is not null && _endpoint.ChannelAddress is { } path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        // _stop and _slots are not disposed: in-flight connection tasks may still observe or release them.
    }
}
