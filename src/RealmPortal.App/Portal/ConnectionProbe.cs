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
/// Connects with <see cref="TcpClient.ConnectAsync(string, int, CancellationToken)"/> and closes the socket.
/// A DNS failure or a refused connection is <see cref="ProbeResult.Failed"/>. The timeout is a cancelled token.
/// </summary>
public sealed class TcpConnectionProbe : IConnectionProbe
{
    public async Task<ProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
            // Connect only: close immediately. Send nothing and download nothing.
            client.Close();
            return ProbeResult.Ok;
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
}
