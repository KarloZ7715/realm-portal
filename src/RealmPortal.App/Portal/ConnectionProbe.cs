using System.Net;
using System.Net.Sockets;

namespace RealmPortal.App.Portal;

/// <summary>Outcome of a connect-only check. Nothing is sent or read.</summary>
public enum ProbeResult
{
    Ok,
    Failed,
    TimedOut,
}

/// <summary>Opens a TCP connection and closes it. Used by the add-server form in portal mode.</summary>
public interface IConnectionProbe
{
    Task<ProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves <paramref name="host"/> and connects to each address until one accepts a TCP connection, then closes it.
/// DNS and the connects share one timeout. A resolver that ignores cancellation still ends as <see cref="ProbeResult.TimedOut"/>
/// and does not open a socket after that. A DNS failure or a refused connection is <see cref="ProbeResult.Failed"/>.
/// </summary>
public sealed class TcpConnectionProbe : IConnectionProbe
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    public TcpConnectionProbe(Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        _resolve = resolve ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
    }

    public async Task<ProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
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
                return ProbeResult.TimedOut;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ProbeResult.TimedOut;
            }
            catch (SocketException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return ProbeResult.TimedOut;
            }
            catch (SocketException)
            {
                return ProbeResult.Failed;
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

    private async Task<ProbeResult> ConnectAnyAsync(string host, int port, CancellationToken token)
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
        catch (SocketException)
        {
            return ProbeResult.Failed;
        }

        // The resolver may ignore the token and return after the probe has already timed out.
        token.ThrowIfCancellationRequested();

        if (addresses is null || addresses.Length == 0)
        {
            return ProbeResult.Failed;
        }

        foreach (var address in addresses)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient(address.AddressFamily);
                await client.ConnectAsync(address, port, token).ConfigureAwait(false);
                client.Close();
                return ProbeResult.Ok;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException("The connection check timed out.", token);
            }
            catch (SocketException)
            {
                // Refused or unreachable. Try the next resolved address.
            }
        }

        return ProbeResult.Failed;
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
