namespace RealmPortal.Core.Service;

/// <summary>
/// The next step a failed call suggests (#194). Like <see cref="ServiceErrorCode"/>, it is a stable code with no text:
/// the UI turns it into a localized hint or a button, and the CLI into a command to run. A code whose cause leaves
/// nothing for the user to do maps to <see cref="None"/>.
/// </summary>
public enum ServiceErrorAction
{
    /// <summary>No useful next step beyond reading the message.</summary>
    None,

    /// <summary>Temporary condition (lock held, file in use, network, rate limit, unexpected failure): try the same action again later.</summary>
    TryAgain,

    /// <summary>The game is running, or that could not be ruled out: close it and try again.</summary>
    CloseGame,

    /// <summary>The server is gone or there is nothing to undo: pick a server from the list.</summary>
    ChooseServer,

    /// <summary>No registered client matches: scan again or add a client folder by hand.</summary>
    ScanOrAddClient,

    /// <summary>The client folder is missing, has no usable <c>Wow.exe</c>, or is an unsupported version: choose another folder.</summary>
    ChooseClientFolder,

    /// <summary>A field of the request is invalid or taken (name, host, realm, account, build, realmlist line, folder): fix it.</summary>
    FixInput,

    /// <summary>The server needs another client build: use a matching client, change the server's build, or force the switch.</summary>
    UseCompatibleClient,

    /// <summary><c>servers.json</c> or <c>clients.json</c> is damaged: check its <c>.bak</c> (<see cref="IPortalService.GetConfigBackups"/>) and restore it.</summary>
    RestoreConfigBackup,

    /// <summary>A file or folder cannot be read or written (permissions, read-only file system): fix access and try again.</summary>
    CheckPermissions,

    /// <summary>The disk is full: free space and try again.</summary>
    FreeDiskSpace,

    /// <summary>Undo stopped on changed client files (<see cref="ServiceError.ConflictingFiles"/>): put them back and undo again, or discard the undo.</summary>
    ResolveUndoConflict,

    /// <summary>Client files and the app state may disagree, or a path is outside the client: review the client and switch again.</summary>
    ReviewClientFiles,

    /// <summary>The game could not be started or the launcher settings are invalid: check the launcher settings.</summary>
    CheckLauncherSettings,
}

/// <summary>Maps a <see cref="ServiceError"/> to its <see cref="ServiceErrorAction"/> (#194). Pure: reads no file.</summary>
public static class ServiceErrorActions
{
    public static ServiceErrorAction NextAction(this ServiceError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code switch
        {
            ServiceErrorCode.StoreLocked or ServiceErrorCode.FileSharingViolation or ServiceErrorCode.HashMismatch
                or ServiceErrorCode.UpdateCheckFailed or ServiceErrorCode.ChecksumMismatch or ServiceErrorCode.AssetMissing
                or ServiceErrorCode.DownloadTooLarge or ServiceErrorCode.UntrustedRedirect or ServiceErrorCode.RateLimited
                or ServiceErrorCode.Unexpected => ServiceErrorAction.TryAgain,
            ServiceErrorCode.GameRunning or ServiceErrorCode.ProcessCheckFailed => ServiceErrorAction.CloseGame,
            ServiceErrorCode.ServerNotFound or ServiceErrorCode.NothingToUndo or ServiceErrorCode.UndoTargetMissing =>
                ServiceErrorAction.ChooseServer,
            ServiceErrorCode.ClientNotFound => ServiceErrorAction.ScanOrAddClient,
            ServiceErrorCode.PathNotFound or ServiceErrorCode.ClientExecutableNotFound or ServiceErrorCode.ClientNotAPortableExecutable
                or ServiceErrorCode.ClientNoVersionResource or ServiceErrorCode.UnsupportedClient => ServiceErrorAction.ChooseClientFolder,
            ServiceErrorCode.InvalidServerInput or ServiceErrorCode.InvalidClientInput or ServiceErrorCode.InvalidRealmlist
                or ServiceErrorCode.ServerNameTaken or ServiceErrorCode.ClientAlreadyExists => ServiceErrorAction.FixInput,
            ServiceErrorCode.IncompatibleClient => ServiceErrorAction.UseCompatibleClient,
            ServiceErrorCode.StoreCorrupt => IsRestorableConfigFile(error.Path) ? ServiceErrorAction.RestoreConfigBackup : ServiceErrorAction.None,
            ServiceErrorCode.PermissionDenied or ServiceErrorCode.ReadOnlyFs or ServiceErrorCode.ClientUnreadable
                or ServiceErrorCode.VerifyFailed => ServiceErrorAction.CheckPermissions,
            ServiceErrorCode.DiskFull => ServiceErrorAction.FreeDiskSpace,
            ServiceErrorCode.UndoConflict => ServiceErrorAction.ResolveUndoConflict,
            ServiceErrorCode.InconsistentState or ServiceErrorCode.PathEscapesClient => ServiceErrorAction.ReviewClientFiles,
            ServiceErrorCode.InvalidLauncherSettings or ServiceErrorCode.LaunchFailed => ServiceErrorAction.CheckLauncherSettings,
            _ => ServiceErrorAction.None, // BackupNotFound, and any code added later until it is mapped
        };
    }

    // Only these two files have a .bak that RestoreConfigBackup can bring back (#196); the UI checks GetConfigBackups for a valid one.
    private static bool IsRestorableConfigFile(string? path) =>
        System.IO.Path.GetFileName(path) is "servers.json" or "clients.json";
}
