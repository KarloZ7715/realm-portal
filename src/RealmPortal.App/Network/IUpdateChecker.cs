namespace RealmPortal.App.Network;

/// <summary>
/// GitHub Releases update check. Backend owns the production type; this App seam matches
/// <c>docs/updates-and-status.md</c>. No channel / IsMandatory.
/// </summary>
public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(UpdateCheckKind kind, CancellationToken cancellationToken = default);

    void RemindLater(UpdateOffer offer);

    void SkipThisVersion(UpdateOffer offer);

    /// <summary>
    /// Download / verify / place (or release-page fallback). Never silent-replaces without user confirmation upstream.
    /// Returns null when the checker cannot install (demo / null double).
    /// </summary>
    Task<UpdateInstallOutcome?> InstallUpdateAsync(UpdateOffer offer, CancellationToken cancellationToken = default);
}
