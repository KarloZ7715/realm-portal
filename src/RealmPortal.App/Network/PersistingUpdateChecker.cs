using RealmPortal.Core.Service;

namespace RealmPortal.App.Network;

/// <summary>
/// Applies <c>network-settings.json</c> gates (auto_check, 24 h, skipped, later) around an inner
/// <see cref="IUpdateChecker"/> using Core <see cref="NetworkSettings"/> (Service store).
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
        return await Task.Run(
            () => CheckCoreAsync(kind, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<UpdateCheckResult> CheckCoreAsync(UpdateCheckKind kind, CancellationToken cancellationToken)
    {
        var loaded = _store.Load();
        if (!loaded.Succeeded)
            return kind == UpdateCheckKind.Manual ? UpdateCheckResult.Failed : UpdateCheckResult.None;
        var settings = loaded.Value!;
        var now = _time.GetUtcNow();

        if (kind == UpdateCheckKind.Automatic)
        {
            if (!settings.UpdatesAutoCheck)
                return UpdateCheckResult.None;
            if (settings.UpdatesLastAttemptUtc is { } last && now - last < AutoCheckInterval)
                return UpdateCheckResult.None;

            var saved = _store.Save(settings with { UpdatesLastAttemptUtc = now });
            if (!saved.Succeeded)
                return UpdateCheckResult.None;
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
        loaded = _store.Load();
        if (!loaded.Succeeded)
            return result;
        settings = loaded.Value!;
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
        PersistChoice(loaded => loaded with
        {
            UpdatesLaterVersion = offer.Version,
            UpdatesLaterUntilUtc = _time.GetUtcNow() + LaterPostpone,
        }, "RemindLater");
    }

    public void SkipThisVersion(UpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        _inner.SkipThisVersion(offer);
        PersistChoice(loaded => loaded with { UpdatesSkippedVersion = offer.Version }, "SkipThisVersion");
    }

    private void PersistChoice(Func<NetworkSettings, NetworkSettings> mutate, string action)
    {
        var loaded = _store.Load();
        if (!loaded.Succeeded)
            throw new InvalidOperationException($"Network settings load failed during {action}: {loaded.Error}");
        var saved = _store.Save(mutate(loaded.Value!));
        if (!saved.Succeeded)
            throw new InvalidOperationException($"Network settings save failed during {action}: {saved.Error}");
    }

    public Task InstallUpdateAsync(UpdateOffer offer, CancellationToken cancellationToken = default)
        => _inner.InstallUpdateAsync(offer, cancellationToken);
}
