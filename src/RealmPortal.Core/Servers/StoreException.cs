namespace RealmPortal.Core.Servers;

/// <summary>The user's config store cannot be read or an operation on it is not allowed.</summary>
public sealed class StoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
