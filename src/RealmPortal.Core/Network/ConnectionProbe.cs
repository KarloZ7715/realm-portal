using System.Net;
using System.Net.Sockets;

namespace RealmPortal.Core.Network;

/// <summary>Outcome of a connect-only check. Nothing is sent or read.</summary>
public enum ProbeResult
{
    Ok,
    Failed,
    TimedOut,
}

/// <summary>
/// Result of <see cref="IConnectionProbe.ProbeAsync"/>, including the <see cref="SocketError"/> when the
/// failure came from DNS or connect so callers can map HostNotFound/NoData to "unknown" instead of offline.
/// </summary>
public readonly record struct ConnectionProbeResult(ProbeResult Outcome, SocketError? SocketError = null)
{
    public static ConnectionProbeResult Ok { get; } = new(ProbeResult.Ok);

    public static ConnectionProbeResult TimedOut { get; } = new(ProbeResult.TimedOut);

    public static ConnectionProbeResult Failed(SocketError? error = null) => new(ProbeResult.Failed, error);

    public static implicit operator ConnectionProbeResult(ProbeResult outcome) =>
        outcome switch
        {
            ProbeResult.Ok => Ok,
            ProbeResult.TimedOut => TimedOut,
            _ => Failed(),
        };
}

/// <summary>Opens a TCP connection and closes it. Used by the add-server form and the status-dot scheduler.</summary>
public interface IConnectionProbe
{
    Task<ConnectionProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves <paramref name="host"/> and connects to each address until one accepts a TCP connection, then closes it.
/// DNS and the connects share one timeout. A resolver that ignores cancellation still ends as <see cref="ProbeResult.TimedOut"/>
/// and does not open a socket after that. DNS <see cref="SocketError.HostNotFound"/> / <see cref="SocketError.NoData"/>
/// and connection errors surface as <see cref="ProbeResult.Failed"/> with the <see cref="SocketError"/> filled in.
/// </summary>
public sealed class TcpConnectionProbe : IConnectionProbe
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Func<IPAddress, int, CancellationToken, Task>? _connect;

    public TcpConnectionProbe(
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        Func<IPAddress, int, CancellationToken, Task>? connect = null)
    {
        _resolve = resolve ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
        _connect = connect;
    }

    public async Task<ConnectionProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            timeoutCts.CancelAfter(timeout);
            var operation = ConnectAnyAsync(host, port, timeoutCts.Token);
            ObserveAbandonedFaults(operation);
            try
            {
                return await operation.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return ConnectionProbeResult.TimedOut;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ConnectionProbeResult.TimedOut;
            }
            catch (SocketException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return ConnectionProbeResult.TimedOut;
            }
            catch (SocketException ex)
            {
                return ConnectionProbeResult.Failed(ex.SocketErrorCode);
            }
        }
        finally
        {
            // WaitAsync can time out before CancelAfter runs. Disposing the source then stops that timer
            // without signaling the token, so a resolver that ignores cancellation could still connect.
            try
            {
                timeoutCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed; the token can no longer be signaled.
            }
        }
    }

    private async Task<ConnectionProbeResult> ConnectAnyAsync(string host, int port, CancellationToken token)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await _resolve(host, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SocketException ex)
        {
            return ConnectionProbeResult.Failed(ex.SocketErrorCode);
        }

        // The resolver may ignore the token and return after the probe has already timed out.
        token.ThrowIfCancellationRequested();

        if (addresses is null || addresses.Length == 0)
        {
            return ConnectionProbeResult.Failed(SocketError.NoData);
        }

        SocketError? lastError = null;
        foreach (var address in addresses)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (_connect is not null)
                {
                    await _connect(address, port, token).ConfigureAwait(false);
                }
                else
                {
                    using var client = new TcpClient(address.AddressFamily);
                    await client.ConnectAsync(address, port, token).ConfigureAwait(false);
                    client.Close();
                }

                return ConnectionProbeResult.Ok;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException("The connection check timed out.", token);
            }
            catch (SocketException ex)
            {
                // Refused or unreachable. Try the next resolved address.
                lastError = ex.SocketErrorCode;
            }
        }

        return ConnectionProbeResult.Failed(lastError);
    }

    /// <summary>
    /// <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/> can return while <paramref name="operation"/> is still running
    /// (a resolver that ignores cancellation). Observe a later fault so it does not surface as unobserved.
    /// </summary>
    private static void ObserveAbandonedFaults(Task operation)
    {
        _ = operation.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
