namespace RealmPortal.Core.Updates;

/// <summary>Stable reasons an update check or download failed (also mirrored on <c>ServiceErrorCode</c>).</summary>
public enum UpdateErrorCode
{
    UpdateCheckFailed,
    ChecksumMismatch,
    AssetMissing,
    DownloadTooLarge,
    UntrustedRedirect,
    RateLimited,
}

/// <summary>Failure of an update check, download or install step.</summary>
public sealed class UpdateException : Exception
{
    public UpdateException(UpdateErrorCode code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    public UpdateErrorCode Code { get; }

    /// <summary>HTTP status when relevant; otherwise null.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>Host only (never a full URL).</summary>
    public string? Host { get; init; }

    public TimeSpan? RetryAfter { get; init; }
}
