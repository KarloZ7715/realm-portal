namespace RealmPortal.App.Network;

/// <summary>
/// Schedules connect-only login-port probes. Backend owns the production type; this App seam matches
/// <c>docs/updates-and-status.md</c> so the UI can wire before Core lands.
/// </summary>
public interface IProbeScheduler : IDisposable
{
    event Action<ServerProbeSnapshot>? StatusChanged;

    bool TryGetStatus(string serverId, out ServerProbeSnapshot snapshot);

    void SetServers(IReadOnlyList<(string ServerId, string Host, int Port)> servers);

    void NotifyListVisible(bool visible);

    void SetEnabled(bool enabled);
}
