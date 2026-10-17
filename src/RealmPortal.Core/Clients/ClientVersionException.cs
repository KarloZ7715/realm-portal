namespace RealmPortal.Core.Clients;

/// <summary>Why the version of a client could not be read.</summary>
public enum ClientVersionErrorKind
{
    /// <summary>The directory does not exist or contains no client executable.</summary>
    ExecutableNotFound,

    /// <summary>The file is not a valid Windows Portable Executable (PE) image.</summary>
    NotAPortableExecutable,

    /// <summary>The executable is a valid PE image but has no usable version resource.</summary>
    NoVersionResource,

    /// <summary>The file exists but cannot be read (permissions, I/O error, invalid path).</summary>
    Unreadable,
}

/// <summary>Thrown by <see cref="IClientVersionReader"/> when a client version cannot be determined.</summary>
public sealed class ClientVersionException : Exception
{
    public ClientVersionException(ClientVersionErrorKind kind, string path, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Path = path;
    }

    /// <summary>The category of failure; callers can branch on it without parsing messages.</summary>
    public ClientVersionErrorKind Kind { get; }

    /// <summary>The directory or file that was being inspected.</summary>
    public string Path { get; }
}
