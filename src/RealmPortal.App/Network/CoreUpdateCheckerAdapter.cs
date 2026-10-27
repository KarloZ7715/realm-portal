using CoreUpdates = RealmPortal.Core.Updates;

namespace RealmPortal.App.Network;

/// <summary>
/// Adapts Core <see cref="CoreUpdates.IUpdateChecker"/> (policy + GitHub + install) to the App dialog seam.
/// Does not wrap with <see cref="PersistingUpdateChecker"/> — Core already uses <c>NetworkSettingsStore.Update</c>.
/// </summary>
public sealed class CoreUpdateCheckerAdapter : IUpdateChecker
{
    private readonly CoreUpdates.IUpdateChecker _inner;

    public CoreUpdateCheckerAdapter(CoreUpdates.IUpdateChecker inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async Task<UpdateCheckResult> CheckAsync(UpdateCheckKind kind, CancellationToken cancellationToken = default)
    {
        var mode = kind == UpdateCheckKind.Automatic
            ? CoreUpdates.UpdateCheckMode.Automatic
            : CoreUpdates.UpdateCheckMode.Manual;
        var result = await _inner.CheckAsync(mode, cancellationToken).ConfigureAwait(false);
        return Map(result, kind);
    }

    public void RemindLater(UpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        _inner.RemindLater(offer.Version);
    }

    public void SkipThisVersion(UpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        _inner.SkipThisVersion(offer.Version);
    }

    public async Task InstallUpdateAsync(UpdateOffer offer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var coreOffer = new CoreUpdates.UpdateOffer(
            offer.Version,
            offer.TagName,
            offer.ReleaseNotesPlain,
            offer.HtmlUrl);
        _ = await _inner.InstallUpdateAsync(coreOffer, cancellationToken).ConfigureAwait(false);
    }

    private static UpdateCheckResult Map(CoreUpdates.UpdateCheckResult result, UpdateCheckKind kind)
    {
        switch (result.Outcome)
        {
            case CoreUpdates.UpdateCheckOutcome.Offer when result.Offer is { } offer:
                return UpdateCheckResult.FromOffer(ToApp(offer));
            case CoreUpdates.UpdateCheckOutcome.UpToDate:
                return UpdateCheckResult.UpToDate;
            case CoreUpdates.UpdateCheckOutcome.RateLimited:
                return UpdateCheckResult.RateLimited;
            case CoreUpdates.UpdateCheckOutcome.Failed:
                return UpdateCheckResult.Failed;
            case CoreUpdates.UpdateCheckOutcome.SkippedByPolicy:
                // Manual debounce surfaces as soft rate-limit; automatic policy skips stay silent.
                if (kind == UpdateCheckKind.Manual
                    && result.Diagnostic is { } d
                    && d.Contains("debounc", StringComparison.OrdinalIgnoreCase))
                    return UpdateCheckResult.RateLimited;
                return kind == UpdateCheckKind.Manual ? UpdateCheckResult.UpToDate : UpdateCheckResult.None;
            case CoreUpdates.UpdateCheckOutcome.NoNotice:
                return kind == UpdateCheckKind.Manual ? UpdateCheckResult.UpToDate : UpdateCheckResult.None;
            default:
                return UpdateCheckResult.None;
        }
    }

    private static UpdateOffer ToApp(CoreUpdates.UpdateOffer offer) =>
        new(offer.Version, offer.TagName, offer.ReleaseNotesPlain, offer.HtmlUrl);
}
