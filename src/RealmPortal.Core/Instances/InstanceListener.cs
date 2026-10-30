using System.IO.Pipes;
using System.Net.Sockets;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.Instances;

/// <summary>
/// The primary instance's side of the channel: accepts one connection at a time, reads one request line (bounded in
/// size and time), calls the handler and writes one reply. Garbage, oversized or slow requests get
/// <see cref="InstanceReply.Invalid"/> (when possible) and never stop the loop.
/// </summary>
internal sealed class InstanceListener : IDisposable
{
    private readonly InstanceEndpoint _endpoint;
    private readonly Func<InstanceMessage, InstanceReply> _handler;
    private readonly TimeSpan _requestTimeout;
    private readonly CancellationTokenSource _stop = new();
    private readonly Socket? _socket;
    private readonly Task _loop;
    private int _disposed;

    private InstanceListener(InstanceEndpoint endpoint, Func<InstanceMessage, InstanceReply> handler, TimeSpan requestTimeout, Socket? socket)
    {
        _endpoint = endpoint;
        _handler = handler;
        _requestTimeout = requestTimeout;
        _socket = socket;
        _loop = Task.Run(socket is null ? PipeLoopAsync : SocketLoopAsync);
    }

    /// <summary>Starts listening. Must only be called while holding the instance lock.</summary>
    public static InstanceListener Start(InstanceEndpoint endpoint, Func<InstanceMessage, InstanceReply> handler, TimeSpan requestTimeout)
    {
        if (endpoint.ChannelAddress is null)
            throw new IOException("No instance channel is available for this directory.");
        if (OperatingSystem.IsWindows())
            return new InstanceListener(endpoint, handler, requestTimeout, null);

        endpoint.EnsureSocketDirectory();
        var path = endpoint.ChannelAddress;

        // We hold the lock, so any existing socket file belongs to a process that is gone: remove it. Anything that is
        // not a socket or a regular file (a directory, a symlink) is left alone and makes the bind fail.
        var existing = new FileInfo(path);
        if (existing.Exists && existing.LinkTarget is null)
            File.Delete(path);

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(path));
            // Only the owner may connect (the directory is 0700 as well).
            File.SetUnixFileMode(path, PrivateStorage.PrivateFileMode);
            socket.Listen(8);
            return new InstanceListener(endpoint, handler, requestTimeout, socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task SocketLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _socket!.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            await using var stream = new NetworkStream(client, ownsSocket: true);
            await ServeAsync(stream).ConfigureAwait(false);
        }
    }

    private async Task PipeLoopAsync()
    {
        var first = true;
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                var options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
                pipe = new NamedPipeServerStream(_endpoint.ChannelAddress!, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, options);
                first = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
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

            await using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }
                catch (IOException)
                {
                    continue;
                }

                await ServeAsync(pipe).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Handles one connection. Never throws.</summary>
    private async Task ServeAsync(Stream stream)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(SingleInstance.ClampTimeout(_requestTimeout));
        try
        {
            var line = await InstanceProtocol.ReadLineAsync(stream, timeout.Token).ConfigureAwait(false);
            var message = line is null ? null : InstanceProtocol.TryDecodeRequest(line);
            var reply = message is null ? InstanceReply.Invalid : Handle(message);
            await stream.WriteAsync(InstanceProtocol.EncodeReply(reply), timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
            if (stream is PipeStream)
            {
                // Closing a Windows pipe handle discards data the client has not read yet, so wait (bounded by the
                // same deadline) until the client closes its end; anything else it sends is ignored.
                var sink = new byte[512];
                while (await stream.ReadAsync(sink, timeout.Token).ConfigureAwait(false) > 0)
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
        _stop.Dispose();
    }
}
