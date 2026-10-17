namespace RealmPortal.Core.ClientFiles;

/// <summary>Machine-readable reason why a client-files operation failed. The UI maps codes to localized text.</summary>
public enum ClientFilesErrorCode
{
    /// <summary>The game is running; its files must not be modified.</summary>
    GameRunning,

    /// <summary>The operating system denied access to a file or directory. Never retried automatically.</summary>
    PermissionDenied,

    /// <summary>The target file system is read-only.</summary>
    ReadOnlyFs,

    /// <summary>The client directory (or a path inside it) does not exist or is not a valid path.</summary>
    PathNotFound,

    /// <summary>A file no longer has the content the plan or backup expects (changed by the user or the game).</summary>
    HashMismatch,

    /// <summary>After writing or restoring, the file content did not match the expected hash.</summary>
    VerifyFailed,

    /// <summary>The client version is not supported (no known file layout for its major version).</summary>
    UnsupportedClient,

    /// <summary>The realmlist text (or the realm name) breaks the contract: non-ASCII, unknown key, quotes, bad first line.</summary>
    InvalidRealmlist,

    /// <summary>There is no applied change to undo for the installation.</summary>
    NothingToUndo,
}

/// <summary>Typed failure of a client-files operation. <see cref="Exception.Message"/> is a diagnostic in English, not UI text.</summary>
public sealed class ClientFilesException : Exception
{
    public ClientFilesException(ClientFilesErrorCode code, string path, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Path = path;
    }

    public ClientFilesErrorCode Code { get; }

    /// <summary>The file or directory involved (may be empty when no single path applies).</summary>
    public string Path { get; }
}
