using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RealmPortal.Core.Instances;

/// <summary>
/// Linux peer check for the instance socket: <c>SO_PEERCRED</c> gives the user id of the process on the other end,
/// which must be the current effective user. Other Unix systems rely on the private (0700) socket folder and the
/// 0600 socket; Windows uses <c>PipeOptions.CurrentUserOnly</c>.
/// </summary>
internal static class PeerCredentials
{
    private const int SolSocket = 1;   // SOL_SOCKET on Linux
    private const int SoPeerCred = 17; // SO_PEERCRED on Linux

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> when the peer is another user (Linux only).</summary>
    public static void Verify(Socket socket, uint? expectedUserId)
    {
        if (!OperatingSystem.IsLinux())
            return;
        var expected = expectedUserId ?? geteuid();
        var peer = PeerUserId(socket);
        if (peer != expected)
            throw new UnauthorizedAccessException($"The instance socket peer runs as user {peer?.ToString() ?? "unknown"}, not {expected}.");
    }

    /// <summary>The peer's user id from <c>struct ucred { pid_t pid; uid_t uid; gid_t gid; }</c>, or null.</summary>
    internal static uint? PeerUserId(Socket socket)
    {
        Span<byte> ucred = stackalloc byte[12];
        try
        {
            // Native byte order, like the kernel wrote it.
            return socket.GetRawSocketOption(SolSocket, SoPeerCred, ucred) >= 8 ? MemoryMarshal.Read<uint>(ucred[4..8]) : null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    internal static uint CurrentUserId() => geteuid();

    [DllImport("libc", SetLastError = false)]
    private static extern uint geteuid();
}
