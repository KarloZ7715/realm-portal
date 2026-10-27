namespace RealmPortal.Core.Servers;

/// <summary>The user's config store cannot be read or an operation on it is not allowed.</summary>
/// <param name="message">English text for logs and diagnostics; not UI text.</param>
/// <param name="innerException">The underlying I/O failure, when there is one.</param>
/// <param name="locked">True when the operation failed because <c>.realm-portal.lock</c> is still held.</param>
/// <param name="contentCorrupt">True when the file exists but its contents are empty or not valid JSON (safe to repair).</param>
public sealed class StoreException(
    string message,
    Exception? innerException = null,
    bool locked = false,
    bool contentCorrupt = false)
    : Exception(message, innerException)
{
    /// <summary>True when another process (or thread) still holds the config-directory lock.</summary>
    public bool Locked { get; } = locked;

    /// <summary>True when the file is empty or not valid JSON (not unsupported version / I/O).</summary>
    public bool ContentCorrupt { get; } = contentCorrupt;
}
