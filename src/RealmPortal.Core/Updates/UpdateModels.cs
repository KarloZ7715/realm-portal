namespace RealmPortal.Core.Updates;

/// <summary>How an update check was triggered.</summary>
public enum UpdateCheckMode
{
    /// <summary>Background daily check; respects <c>updates.auto_check</c> and the 24 h gate.</summary>
    Automatic = 0,

    /// <summary>User pressed Check for updates; works even when auto-check is off; debounced to 1/min.</summary>
    Manual = 1,
}

/// <summary>Outcome of <see cref="IUpdateChecker.CheckAsync"/>.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>Running version is current (or release is not newer).</summary>
    UpToDate = 0,

    /// <summary>A newer release should be offered to the user.</summary>
    Offer = 1,

    /// <summary>No notice: skipped, postponed (Later), draft/prerelease/bad tag, or not strictly greater.</summary>
    NoNotice = 2,

    /// <summary>Check failed silently for automatic mode; Manual may surface the error.</summary>
    Failed = 3,

    /// <summary>GitHub rate limit; honour Retry-After before trying again.</summary>
    RateLimited = 4,

    /// <summary>Automatic check skipped because auto_check is off or within 24 h of the last attempt.</summary>
    SkippedByPolicy = 5,
}

/// <summary>A release the UI may show in the update dialog / banner. Never mandatory.</summary>
/// <param name="Version">Parsed SemVer without the leading <c>v</c> (e.g. <c>1.1.0</c>).</param>
/// <param name="TagName">Original tag when available (e.g. <c>v1.1.0</c>); informational.</param>
/// <param name="ReleaseNotesPlain">Plain-text body, truncated; no HTML.</param>
/// <param name="HtmlUrl">Validated release page URL, or null to build from the version.</param>
public sealed record UpdateOffer(
    string Version,
    string? TagName,
    string ReleaseNotesPlain,
    string? HtmlUrl);

/// <summary>Result of an update check.</summary>
public sealed record UpdateCheckResult(
    UpdateCheckOutcome Outcome,
    UpdateOffer? Offer = null,
    UpdateErrorCode? ErrorCode = null,
    string? Diagnostic = null,
    int? HttpStatus = null);

/// <summary>Where a verified download was placed, plus optional copyable shell command / restart hint.</summary>
public sealed record UpdateInstallResult(
    string SavedPath,
    InstallKind Kind,
    bool ReplacedAppImage = false,
    string? CopyableCommand = null,
    string? BackupPath = null,
    string? Instructions = null);
