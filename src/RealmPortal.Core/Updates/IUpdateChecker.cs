namespace RealmPortal.Core.Updates;

/// <summary>
/// Core update-notice API for the UI. Dialogs / banner / toggles stay in the App layer.
/// There is no mandatory update — offers are always optional.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Checks GitHub for a newer release. Automatic mode respects <c>updates.auto_check</c> and the 24 h gate
    /// and fails silently. Manual mode always attempts a request (debounced to one per minute) even when auto-check is off.
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(UpdateCheckMode mode, CancellationToken cancellationToken = default);

    /// <summary>Postpone <paramref name="version"/> for 3 days (<c>updates.later_version</c> / <c>later_until_utc</c>).</summary>
    void RemindLater(string version);

    /// <summary>Never offer <paramref name="version"/> again (<c>updates.skipped_version</c>).</summary>
    void SkipThisVersion(string version);

    /// <summary>
    /// Download, verify (SHA256SUMS) and place the asset for the current <see cref="InstallKind"/>.
    /// Does not execute the download except after a verified AppImage self-replace when the user restarts.
    /// </summary>
    Task<UpdateInstallResult> InstallUpdateAsync(UpdateOffer offer, CancellationToken cancellationToken = default);
}
