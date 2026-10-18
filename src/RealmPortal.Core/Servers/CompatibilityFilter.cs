using RealmPortal.Core.Clients;
using RealmPortal.Core.Service;

namespace RealmPortal.Core.Servers;

/// <summary>Decides which servers a given game client can play on.</summary>
public static class CompatibilityFilter
{
    /// <summary>
    /// A server without a required build accepts any client. A server that requires a build is compatible
    /// only when the client's <see cref="ClientVersion.Build"/> is known and equal to it
    /// (an unknown client version cannot be verified, so it is not compatible).
    /// </summary>
    public static bool IsCompatible(Server server, ClientVersion? client)
    {
        ArgumentNullException.ThrowIfNull(server);
        return server.RequiredClientBuild is not { } required || client?.Build == required;
    }

    /// <summary>
    /// Same rule as <see cref="IsCompatible(Server, ClientVersion?)"/> for a <see cref="ServerEntry"/>,
    /// which carries <see cref="ServerEntry.RequiredClientBuild"/> but is not a <see cref="Server"/>.
    /// </summary>
    public static bool IsCompatible(ServerEntry server, ClientVersion? client)
    {
        ArgumentNullException.ThrowIfNull(server);
        return server.RequiredClientBuild is not { } required || client?.Build == required;
    }

    /// <summary>Returns the compatible servers, keeping the input order.</summary>
    public static IReadOnlyList<Server> Filter(IEnumerable<Server> servers, ClientVersion? client) =>
        servers.Where(server => IsCompatible(server, client)).ToList();
}
