using RealmPortal.Core.IO;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;

namespace RealmPortal.Cli;

internal static class ServiceCli
{
    /// <summary>
    /// One line for a failed service call: the cause, then (when there is one) a next step the user can take, usually a
    /// command to run (#194). <paramref name="verbose"/> swaps the cause for the diagnostic text of the codes that carry
    /// file paths or parse errors; the next step stays.
    /// </summary>
    public static string Describe(ServiceError error, bool verbose = false)
    {
        var cause = Cause(error, verbose);
        var next = NextStep(error, cause);
        return next is null ? cause : $"{cause} {next}";
    }

    private static string Cause(ServiceError error, bool verbose)
    {
        if (verbose && !string.IsNullOrEmpty(error.Diagnostic)
            && error.Code is ServiceErrorCode.StoreCorrupt
                or ServiceErrorCode.StoreLocked
                or ServiceErrorCode.PermissionDenied
                or ServiceErrorCode.ReadOnlyFs
                or ServiceErrorCode.Unexpected
                or ServiceErrorCode.InconsistentState)
        {
            return EndSentence(UserFacingMessages.Sanitize(error.Diagnostic));
        }

        return error.Code switch
        {
            ServiceErrorCode.ServerNameTaken => $"A server named '{error.ServerName}' already exists.",
            ServiceErrorCode.ServerNotFound => "No server matches the selector; it may have been removed.",
            ServiceErrorCode.ClientNotFound => string.IsNullOrEmpty(error.Path)
                ? "No registered client matches the request."
                : $"No registered client uses the folder '{error.Path}'.",
            ServiceErrorCode.ClientAlreadyExists => "A client with that folder is already registered.",
            ServiceErrorCode.InvalidClientInput => string.IsNullOrEmpty(error.Diagnostic)
                ? "Client input is invalid."
                : EndSentence(UserFacingMessages.Sanitize(error.Diagnostic)),
            ServiceErrorCode.IncompatibleClient =>
                $"Server requires client build {error.RequiredBuild}, but the client is {error.ClientBuild}.",
            ServiceErrorCode.NothingToUndo => "Nothing to undo: there is no earlier switch to go back to.",
            ServiceErrorCode.UndoTargetMissing => "The previous server no longer exists; cannot undo.",
            ServiceErrorCode.GameRunning => "The game is running, so its client files and active server cannot be changed.",
            ServiceErrorCode.ProcessCheckFailed => string.IsNullOrEmpty(error.Diagnostic)
                ? "Could not check whether the game is running, so nothing was changed."
                : EndSentence(error.Diagnostic),
            ServiceErrorCode.LaunchFailed => string.IsNullOrEmpty(error.Diagnostic) ? "Launch failed." : EndSentence(error.Diagnostic),
            ServiceErrorCode.InvalidRealmlist => string.IsNullOrEmpty(error.Diagnostic) ? "The server's realmlist is invalid." : error.Diagnostic,
            ServiceErrorCode.StoreCorrupt => FormatStoreCorrupt(error),
            ServiceErrorCode.StoreLocked => "Another Realm Portal process is using the config directory; wait for it and try again.",
            ServiceErrorCode.InconsistentState => "The app state may be inconsistent: a step failed and could not be rolled back.",
            ServiceErrorCode.UndoConflict => error.ConflictingFiles is { Count: > 0 } files
                ? $"Client files changed since the switch, so undo stopped without overwriting them: {string.Join(", ", files)}."
                : "Client files changed since the switch, so undo stopped without overwriting them.",
            ServiceErrorCode.PermissionDenied => string.IsNullOrEmpty(error.Path)
                ? "Permission denied: a file or folder Realm Portal needs cannot be read or written."
                : $"Permission denied: '{error.Path}' cannot be read or written.",
            ServiceErrorCode.ReadOnlyFs => "The file system or directory is read-only.",
            ServiceErrorCode.DiskFull => "There is not enough disk space to complete the operation.",
            ServiceErrorCode.FileSharingViolation => "A file is in use by another process.",
            ServiceErrorCode.PathNotFound => string.IsNullOrEmpty(error.Path)
                ? "A required path was not found."
                : $"Path not found: '{error.Path}'.",
            ServiceErrorCode.BackupNotFound => FormatBackupNotFound(error),
            ServiceErrorCode.PathEscapesClient => string.IsNullOrEmpty(error.Diagnostic)
                ? "The path is not a managed client file."
                : EndSentence(error.Diagnostic),
            ServiceErrorCode.HashMismatch => "A client file changed since it was planned or backed up.",
            ServiceErrorCode.VerifyFailed => "A client file could not be verified after writing.",
            ServiceErrorCode.UnsupportedClient => string.IsNullOrEmpty(error.ClientVersion)
                ? "This client version is not supported, so Realm Portal does not change its files."
                : $"This client version is not supported ({error.ClientVersion}), so Realm Portal does not change its files.",
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
                : EndSentence(error.Diagnostic),
            ServiceErrorCode.UpdateCheckFailed => "The update check failed.",
            ServiceErrorCode.ChecksumMismatch => "The downloaded update did not match its checksum, so it was not installed.",
            ServiceErrorCode.AssetMissing => "The expected update file is missing from the release.",
            ServiceErrorCode.DownloadTooLarge => "The update download exceeded the allowed size, so it was not installed.",
            ServiceErrorCode.UntrustedRedirect => "The update URL was outside the allowed GitHub hosts, so nothing was downloaded.",
            ServiceErrorCode.RateLimited => "GitHub rate-limited the update check.",
            ServiceErrorCode.Unexpected => "An unexpected error occurred.",
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Code, "Unhandled ServiceErrorCode in CLI messages."),
        };
    }

    /// <summary>
    /// The next step for <paramref name="error"/>, in line with <see cref="ServiceErrorActions.NextAction"/> but written as CLI
    /// commands; <c>null</c> when there is none or <paramref name="cause"/> already names one.
    /// </summary>
    internal static string? NextStep(ServiceError error, string cause)
    {
        // The message already carries its own command (config restore, --line hints and so on).
        if (cause.Contains("realm-portal ", StringComparison.Ordinal))
            return null;

        return error.Code switch
        {
            ServiceErrorCode.ServerNameTaken => "Choose another name, or run 'realm-portal list' to see the existing servers.",
            ServiceErrorCode.ServerNotFound => "Run 'realm-portal list' to see the current servers.",
            ServiceErrorCode.ClientNotFound =>
                "Run 'realm-portal client list' to see the registered clients, 'realm-portal scan' to look for clients, or 'realm-portal client add <path>' to add one by hand.",
            ServiceErrorCode.ClientAlreadyExists => "Run 'realm-portal client list' to find it.",
            ServiceErrorCode.InvalidClientInput or ServiceErrorCode.InvalidServerInput => "Fix the value and run the command again.",
            ServiceErrorCode.IncompatibleClient =>
                "Use a client with that build, change the server's build with 'realm-portal edit <server> --build <n|any>', or add --force to switch anyway.",
            ServiceErrorCode.NothingToUndo => "Run 'realm-portal status' to see the active server, or 'realm-portal switch <server>' to change it.",
            ServiceErrorCode.UndoTargetMissing => "Run 'realm-portal switch <server>' to choose another server.",
            ServiceErrorCode.GameRunning or ServiceErrorCode.ProcessCheckFailed => "Close the game and run the command again.",
            ServiceErrorCode.LaunchFailed =>
                "Check the --wrapper command or the launcher settings; 'realm-portal launch --dry-run' shows the command that would run.",
            ServiceErrorCode.InvalidRealmlist => "Fix the host or the extra realmlist lines (--line) and run the command again.",
            ServiceErrorCode.StoreCorrupt => RestorableStore(error) is { } store
                ? $"A previous copy exists; run 'realm-portal config restore {store}' to restore it."
                : "Realm Portal does not overwrite it: fix the file by hand, or move it out of the config folder to start it over (add --verbose for details).",
            ServiceErrorCode.InconsistentState =>
                "Run 'realm-portal status' to see the active server, then 'realm-portal switch <server>' to write its client files again.",
            ServiceErrorCode.UndoConflict =>
                "Put those files back as they were after the switch, or move your changes elsewhere, and run 'realm-portal undo' again.",
            ServiceErrorCode.PermissionDenied =>
                "Check that your user owns the config and client folders and can write to them, then try again (add --verbose for the path).",
            ServiceErrorCode.ReadOnlyFs => "Make the folder writable or move it to a writable disk, then try again.",
            ServiceErrorCode.DiskFull => "Free some disk space and try again.",
            ServiceErrorCode.FileSharingViolation => "Close the program using it (often the game or an editor) and try again.",
            ServiceErrorCode.PathNotFound =>
                "Check that the folder exists; if a client folder moved, run 'realm-portal client edit <client> --path <new folder>'.",
            ServiceErrorCode.PathEscapesClient =>
                "Nothing was written. Check that the client folder has no links pointing outside it and that it is the folder the switch was made for.",
            ServiceErrorCode.HashMismatch => "Run the command again so Realm Portal plans with the current files.",
            ServiceErrorCode.VerifyFailed => "Check the disk and the permissions of the client folder, then try again.",
            ServiceErrorCode.UnsupportedClient =>
                "Use a supported client, or run 'realm-portal switch <server>' without --client-dir to change only the active server.",
            ServiceErrorCode.ClientExecutableNotFound => "Choose the game folder that contains Wow.exe.",
            ServiceErrorCode.ClientNotAPortableExecutable or ServiceErrorCode.ClientNoVersionResource =>
                "The file may be damaged or may not be the game; choose the folder of a working client.",
            ServiceErrorCode.ClientUnreadable => "Check the folder's permissions and try again.",
            ServiceErrorCode.InvalidLauncherSettings =>
                "Check the --wrapper command or the launcher settings; 'realm-portal launch --dry-run' shows the command that would run.",
            ServiceErrorCode.UpdateCheckFailed => "Check your internet connection and try again later.",
            ServiceErrorCode.ChecksumMismatch or ServiceErrorCode.AssetMissing or ServiceErrorCode.DownloadTooLarge
                or ServiceErrorCode.UntrustedRedirect or ServiceErrorCode.RateLimited => "Try again later.",
            ServiceErrorCode.Unexpected => "Run the command again with --verbose for details.",
            _ => null,
        };
    }

    /// <summary>Adds a final period so a next step can follow the cause as its own sentence.</summary>
    private static string EndSentence(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length == 0 || trimmed[^1] is '.' or '!' or '?' ? trimmed : trimmed + ".";
    }

    private static string FormatBackupNotFound(ServiceError error)
    {
        // #196: a missing config .bak (servers.json.bak, clients.json.bak) gets its own message.
        var name = error.Path is { } p ? Path.GetFileName(p) : null;
        if (name is "servers.json" + ConfigFileBackup.BackupSuffix or "clients.json" + ConfigFileBackup.BackupSuffix)
        {
            var store = name[..^ConfigFileBackup.BackupSuffix.Length];
            return $"{store} has no {ConfigFileBackup.BackupSuffix} copy yet; one is kept on the next save that changes it.";
        }

        return "The backup was not found; it may have been deleted.";
    }

    private static string FormatStoreCorrupt(ServiceError error)
    {
        var name = error.Path is { } p ? Path.GetFileName(p) : null;
        return name is null or "" ? "A config file is corrupt or unreadable." : $"Config file '{name}' is corrupt or unreadable.";
    }

    /// <summary>#196: <c>servers</c> or <c>clients</c> when the damaged file has a valid <c>.bak</c> that <c>config restore</c> can bring back.</summary>
    private static string? RestorableStore(ServiceError error)
    {
        var name = error.Path is { } p ? Path.GetFileName(p) : null;
        var store = name switch { "servers.json" => "servers", "clients.json" => "clients", _ => null };
        Func<byte[], bool>? isValid = store switch
        {
            "servers" => ServerStore.IsValidContent,
            "clients" => RealmPortal.Core.Clients.ClientStore.IsValidContent,
            _ => null,
        };
        return isValid is not null && ConfigFileBackup.Inspect(error.Path + ConfigFileBackup.BackupSuffix, isValid) == ConfigFileState.Valid
            ? store
            : null;
    }

    private static string FormatInvalidServerInput(ServiceError error)
    {
        if (!string.IsNullOrEmpty(error.Diagnostic))
            return EndSentence(UserFacingMessages.Sanitize(error.Diagnostic));
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
