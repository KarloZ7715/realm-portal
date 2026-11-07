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

    /// <summary>The release has no <c>SHA256SUMS.minisig</c>, so its checksums cannot be trusted (#116).</summary>
    SignatureMissing,

    /// <summary><c>SHA256SUMS.minisig</c> is there but does not verify: bad signature, unknown key, wrong release, too large or malformed.</summary>
    SignatureInvalid,
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

    /// <summary>Why the release signature was rejected, for <see cref="UpdateErrorCode.SignatureInvalid"/>.</summary>
    public Signing.SignatureStatus? Signature { get; init; }
}
