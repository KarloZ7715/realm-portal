using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;

namespace RealmPortal.Cli;

internal static class ServiceCli
{
    public static string Describe(ServiceError error, bool verbose = false)
    {
        if (verbose && !string.IsNullOrEmpty(error.Diagnostic)
            && error.Code is ServiceErrorCode.StoreCorrupt
                or ServiceErrorCode.StoreLocked
                or ServiceErrorCode.PermissionDenied
                or ServiceErrorCode.ReadOnlyFs
                or ServiceErrorCode.Unexpected
                or ServiceErrorCode.InconsistentState)
        {
            return UserFacingMessages.Sanitize(error.Diagnostic);
        }

        return error.Code switch
        {
            ServiceErrorCode.ServerNameTaken => $"A server named '{error.ServerName}' already exists.",
            ServiceErrorCode.ServerNotFound => "no server matches the selector.",
            ServiceErrorCode.ClientNotFound => "No registered client matches the request.",
            ServiceErrorCode.ClientAlreadyExists => "A client with that folder is already registered.",
            ServiceErrorCode.InvalidClientInput => "Client input is invalid.",
            ServiceErrorCode.IncompatibleClient =>
                $"Server requires client build {error.RequiredBuild}, but the client is {error.ClientBuild} (use --force).",
            ServiceErrorCode.NothingToUndo => "Nothing to undo.",
            ServiceErrorCode.UndoTargetMissing => "The previous server no longer exists; cannot undo.",
            ServiceErrorCode.GameRunning => "The game is running; close it before changing this client or an active server.",
            ServiceErrorCode.ProcessCheckFailed => string.IsNullOrEmpty(error.Diagnostic)
                ? "Could not check whether the game is running."
                : error.Diagnostic,
            ServiceErrorCode.LaunchFailed => string.IsNullOrEmpty(error.Diagnostic) ? "Launch failed." : error.Diagnostic,
            ServiceErrorCode.InvalidRealmlist => error.Diagnostic ?? "The server's realmlist is invalid.",
            ServiceErrorCode.StoreCorrupt => FormatStoreCorrupt(error),
            ServiceErrorCode.StoreLocked => "Another Realm Portal process is using the config directory; wait for it and try again.",
            ServiceErrorCode.InconsistentState => "The app state may be inconsistent; review config and client files.",
            ServiceErrorCode.UndoConflict => "Client files changed since the switch; undo stopped.",
            ServiceErrorCode.PermissionDenied => "Permission denied.",
            ServiceErrorCode.ReadOnlyFs => "The file system or directory is read-only.",
            ServiceErrorCode.DiskFull => "There is not enough disk space to complete the operation.",
            ServiceErrorCode.FileSharingViolation => "A file is in use by another process.",
            ServiceErrorCode.PathNotFound => string.IsNullOrEmpty(error.Path)
                ? "A required path was not found."
                : $"Path not found: '{error.Path}'.",
            ServiceErrorCode.BackupNotFound => "The backup was not found.",
            ServiceErrorCode.PathEscapesClient => string.IsNullOrEmpty(error.Diagnostic)
                ? "The path is not a managed client file."
                : error.Diagnostic,
            ServiceErrorCode.HashMismatch => "A client file changed since it was planned or backed up.",
            ServiceErrorCode.VerifyFailed => "A client file could not be verified after writing.",
            ServiceErrorCode.UnsupportedClient => string.IsNullOrEmpty(error.ClientVersion)
                ? "This client version is not supported."
                : $"This client version is not supported ({error.ClientVersion}).",
            ServiceErrorCode.ClientExecutableNotFound => string.IsNullOrEmpty(error.Path)
                ? "Wow.exe was not found in the client folder."
                : $"Wow.exe was not found in '{error.Path}'.",
            ServiceErrorCode.ClientNotAPortableExecutable => "Wow.exe is not a valid portable executable.",
            ServiceErrorCode.ClientNoVersionResource => "Wow.exe has no readable version resource.",
            ServiceErrorCode.ClientUnreadable => string.IsNullOrEmpty(error.Path)
                ? "The client folder could not be read."
                : $"The client folder could not be read: '{error.Path}'.",
            ServiceErrorCode.InvalidServerInput => FormatInvalidServerInput(error),
            ServiceErrorCode.InvalidLauncherSettings => string.IsNullOrEmpty(error.Diagnostic)
                ? "Launcher settings are invalid."
                : error.Diagnostic,
            ServiceErrorCode.UpdateCheckFailed => "The update check failed.",
            ServiceErrorCode.ChecksumMismatch => "The downloaded update did not match its checksum.",
            ServiceErrorCode.AssetMissing => "The expected update file is missing from the release.",
            ServiceErrorCode.DownloadTooLarge => "The update download exceeded the allowed size.",
            ServiceErrorCode.UntrustedRedirect => "The update URL was outside the allowed GitHub hosts.",
            ServiceErrorCode.RateLimited => "GitHub rate-limited the update check; try again later.",
            ServiceErrorCode.Unexpected => "An unexpected error occurred.",
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Code, "Unhandled ServiceErrorCode in CLI messages."),
        };
    }

    private static string FormatStoreCorrupt(ServiceError error)
    {
        var name = error.Path is { } p ? Path.GetFileName(p) : null;
        return name is null or ""
            ? "A config file is corrupt or unreadable."
            : $"Config file '{name}' is corrupt or unreadable.";
    }

    private static string FormatInvalidServerInput(ServiceError error)
    {
        if (!string.IsNullOrEmpty(error.Diagnostic))
            return UserFacingMessages.Sanitize(error.Diagnostic);
        return error.Field switch
        {
            "name" => "Server name is invalid.",
            "host" => "Server host is invalid.",
            "realm" => "Realm name is invalid.",
            "account" => "Account name is invalid.",
            "build" => "Client build must be a positive number.",
            _ => "Server input is invalid.",
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
