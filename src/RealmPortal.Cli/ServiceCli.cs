using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;

namespace RealmPortal.Cli;

internal static class ServiceCli
{
    public static string Describe(ServiceError error, bool verbose = false)
    {
        return error.Code switch
        {
            ServiceErrorCode.ServerNameTaken => $"A server named '{error.ServerName}' already exists.",
            ServiceErrorCode.ServerNotFound => "no server matches the selector.",
            ServiceErrorCode.ClientNotFound => "No registered client matches the request.",
            ServiceErrorCode.IncompatibleClient =>
                $"Server requires client build {error.RequiredBuild}, but the client is {error.ClientBuild} (use --force).",
            ServiceErrorCode.NothingToUndo => "Nothing to undo.",
            ServiceErrorCode.UndoTargetMissing => "The previous server no longer exists; cannot undo.",
            ServiceErrorCode.GameRunning => "The game is running; close it before changing client files.",
            ServiceErrorCode.LaunchFailed => string.IsNullOrEmpty(error.Diagnostic) ? "Launch failed." : error.Diagnostic,
            ServiceErrorCode.InvalidRealmlist => error.Diagnostic,
            ServiceErrorCode.StoreCorrupt => verbose && !string.IsNullOrEmpty(error.Diagnostic)
                ? error.Diagnostic
                : "A config file is corrupt or unreadable.",
            ServiceErrorCode.InconsistentState => verbose && !string.IsNullOrEmpty(error.Diagnostic)
                ? error.Diagnostic
                : "The app state may be inconsistent; review config and client files.",
            ServiceErrorCode.UndoConflict => "Client files changed since the switch; undo stopped.",
            ServiceErrorCode.PermissionDenied => verbose && !string.IsNullOrEmpty(error.Diagnostic)
                ? error.Diagnostic
                : "Permission denied.",
            ServiceErrorCode.ReadOnlyFs => verbose && !string.IsNullOrEmpty(error.Diagnostic)
                ? error.Diagnostic
                : "The file system or directory is read-only.",
            ServiceErrorCode.Unexpected => verbose && !string.IsNullOrEmpty(error.Diagnostic)
                ? error.Diagnostic
                : "An unexpected error occurred.",
            _ => verbose && !string.IsNullOrEmpty(error.Diagnostic) ? error.Diagnostic : error.Code.ToString(),
        };
    }

    public static ServerEntry? FindServerEntry(IReadOnlyList<ServerEntry> servers, string selector)
    {
        var found = servers.FirstOrDefault(s => s.Id == selector)
                    ?? servers.FirstOrDefault(s => string.Equals(s.Name, selector, StringComparison.OrdinalIgnoreCase));
        if (found is not null || selector.Length == 0)
            return found;

        var byPrefix = servers.Where(s => s.Id.StartsWith(selector, StringComparison.Ordinal)).Take(2).ToList();
        return byPrefix.Count == 1 ? byPrefix[0] : null;
    }
}
