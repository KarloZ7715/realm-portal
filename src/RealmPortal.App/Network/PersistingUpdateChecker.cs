namespace RealmPortal.App.Network;

/// <summary>
/// Applies <c>network-settings.json</c> gates (auto_check, 24 h, skipped, later) around an inner
/// <see cref="IUpdateChecker"/>. Backend's production checker may absorb this; the UI keeps the seam.
/// </summary>
public sealed class PersistingUpdateChecker : IUpdateChecker
{
    public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan LaterPostpone = TimeSpan.FromDays(3);
    public static readonly TimeSpan ManualDebounce = TimeSpan.FromMinutes(1);

    private readonly IUpdateChecker _inner;
    private readonly INetworkSettingsStore _store;
    private readonly TimeProvider _time;
    private DateTimeOffset? _lastManualAttemptUtc;
    private readonly object _gate = new();

    public PersistingUpdateChecker(IUpdateChecker inner, INetworkSettingsStore store, TimeProvider? time = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _time = time ?? TimeProvider.System;
    }

    public async Task<UpdateCheckResult> CheckAsync(UpdateCheckKind kind, CancellationToken cancellationToken = default)
    {
        var settings = _store.Load();
        var now = _time.GetUtcNow();

        if (kind == UpdateCheckKind.Automatic)
        {
            if (!settings.UpdatesAutoCheck)
                return UpdateCheckResult.None;
            if (settings.UpdatesLastAttemptUtc is { } last && now - last < AutoCheckInterval)
                return UpdateCheckResult.None;

            settings.UpdatesLastAttemptUtc = now;
            _store.Save(settings);
        }
        else
        {
            lock (_gate)
            {
                if (_lastManualAttemptUtc is { } manual && now - manual < ManualDebounce)
                    return UpdateCheckResult.RateLimited;
                _lastManualAttemptUtc = now;
            }
        }

        var result = await _inner.CheckAsync(kind, cancellationToken).ConfigureAwait(false);
        if (result.Kind != UpdateCheckResultKind.Offer || result.Offer is null)
            return result;

        var offer = result.Offer;
        settings = _store.Load();
        if (string.Equals(offer.Version, settings.UpdatesSkippedVersion, StringComparison.Ordinal))
            return kind == UpdateCheckKind.Manual ? UpdateCheckResult.UpToDate : UpdateCheckResult.None;

        if (string.Equals(offer.Version, settings.UpdatesLaterVersion, StringComparison.Ordinal)
            && settings.UpdatesLaterUntilUtc is { } until
            && until > now)
            return UpdateCheckResult.None;

        return result;
    }

    public void RemindLater(UpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        _inner.RemindLater(offer);
        var settings = _store.Load();
        settings.UpdatesLaterVersion = offer.Version;
        settings.UpdatesLaterUntilUtc = _time.GetUtcNow() + LaterPostpone;
        _store.Save(settings);
    }

    public void SkipThisVersion(UpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        _inner.SkipThisVersion(offer);
        var settings = _store.Load();
        settings.UpdatesSkippedVersion = offer.Version;
        _store.Save(settings);
    }

    public Task InstallUpdateAsync(UpdateOffer offer, CancellationToken cancellationToken = default)
        => _inner.InstallUpdateAsync(offer, cancellationToken);
}
