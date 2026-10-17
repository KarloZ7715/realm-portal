namespace RealmPortal.Core.Servers;

/// <summary>Why a realmlist text was rejected.</summary>
public enum InvalidRealmlistKind
{
    /// <summary>The text is null or empty.</summary>
    Empty,

    /// <summary>The text contains a non-ASCII character.</summary>
    NotAscii,

    /// <summary>The first line is not exactly <c>set realmlist &lt;host&gt;</c> in lowercase.</summary>
    WrongFirstLine,

    /// <summary>A line is not of the form <c>set &lt;key&gt; &lt;value&gt;</c>.</summary>
    MalformedLine,

    /// <summary>The key is not <c>realmlist</c> or <c>patchlist</c> (this includes <c>portal</c>, not supported yet).</summary>
    ForbiddenKey,

    /// <summary><c>realmlist</c> or <c>patchlist</c> appears more than once.</summary>
    DuplicateKey,

    /// <summary>A value contains a quote, a space or a control character.</summary>
    InvalidValue,

    /// <summary>A line does not end with CR LF, or a bare CR/LF is present.</summary>
    BadLineEnding,
}

/// <summary>
/// The realmlist text breaks the realmlist contract. Raised when a server is created and when it is applied
/// (switch/undo); when it is raised nothing has been written.
/// </summary>
public sealed class InvalidRealmlistException : Exception
{
    public InvalidRealmlistException(InvalidRealmlistKind kind, string reason, int? lineNumber = null, string? serverName = null)
        : base(BuildMessage(kind, reason, lineNumber, serverName))
    {
        Kind = kind;
        Reason = reason;
        LineNumber = lineNumber;
        ServerName = serverName;
    }

    /// <summary>The category of the violation; callers can branch on it without parsing messages.</summary>
    public InvalidRealmlistKind Kind { get; }

    /// <summary>Human-readable explanation without the server name or line prefix.</summary>
    public string Reason { get; }

    /// <summary>1-based line of the realmlist that caused the error, when it is tied to one line.</summary>
    public int? LineNumber { get; }

    /// <summary>Name of the server whose realmlist is invalid, when known (set when applying a stored server).</summary>
    public string? ServerName { get; }

    /// <summary>The same error attributed to a server.</summary>
    public InvalidRealmlistException ForServer(string serverName) => new(Kind, Reason, LineNumber, serverName);

    private static string BuildMessage(InvalidRealmlistKind kind, string reason, int? lineNumber, string? serverName) =>
        (serverName is null ? "Invalid realmlist" : $"Server '{serverName}' has an invalid realmlist")
        + (lineNumber is { } n ? $" (line {n})" : "")
        + $": {reason} [{kind}]";
}
