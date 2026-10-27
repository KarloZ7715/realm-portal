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

    /// <summary>Opens the release page or starts install; never silent-replaces the binary from this seam.</summary>
    Task InstallUpdateAsync(UpdateOffer offer, CancellationToken cancellationToken = default);
}
