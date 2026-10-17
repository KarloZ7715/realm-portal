namespace RealmPortal.Core.Clients;

/// <summary>
/// Reports the version of a game client installed in a directory.
/// Implemented by the Systems part (client detection); Core and the CLI only consume it.
/// </summary>
public interface IClientVersionProvider
{
    /// <summary>
    /// Returns the version of the client found in <paramref name="clientDirectory"/>,
    /// or <c>null</c> when the directory holds no recognisable client or the version cannot be determined.
    /// Implementations must not throw for a missing or unreadable directory.
    /// </summary>
    ClientVersion? GetClientVersion(string clientDirectory);
}
