namespace RealmPortal.Core.Servers;

/// <summary>The user's config store cannot be read or an operation on it is not allowed.</summary>
/// <param name="message">English text for logs and diagnostics; not UI text.</param>
/// <param name="innerException">The underlying I/O failure, when there is one.</param>
/// <param name="locked">True when the operation failed because <c>.realm-portal.lock</c> is still held.</param>
public sealed class StoreException(string message, Exception? innerException = null, bool locked = false)
    : Exception(message, innerException)
{
    /// <summary>True when another process (or thread) still holds the config-directory lock.</summary>
    public bool Locked { get; } = locked;
}
