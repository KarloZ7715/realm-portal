using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Service;

/// <summary>Shared English text for client-folder drift (CLI status and undo output).</summary>
public static class ClientSyncMessages
{
    public static string DriftLine(Server active, string clientPath) => DriftLine(active.Name, clientPath);

    public static string DriftLine(string activeServerName, string clientPath) =>
        $"Client folder out of sync: {clientPath} (run 'switch {activeServerName} --client-dir <folder>')";
}
