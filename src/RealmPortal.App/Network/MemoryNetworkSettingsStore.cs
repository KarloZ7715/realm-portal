namespace RealmPortal.App.Network;

/// <summary>In-memory <see cref="INetworkSettingsStore"/> for tests and the demo shell.</summary>
public sealed class MemoryNetworkSettingsStore : INetworkSettingsStore
{
    private NetworkSettings _settings = new();

    public NetworkSettings Load() => Clone(_settings);

    public void Save(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = Clone(settings);
    }

    private static NetworkSettings Clone(NetworkSettings s) => new()
    {
        Version = s.Version,
        UpdatesAutoCheck = s.UpdatesAutoCheck,
        UpdatesLastAttemptUtc = s.UpdatesLastAttemptUtc,
        UpdatesEtag = s.UpdatesEtag,
        UpdatesSkippedVersion = s.UpdatesSkippedVersion,
        UpdatesLaterVersion = s.UpdatesLaterVersion,
        UpdatesLaterUntilUtc = s.UpdatesLaterUntilUtc,
        StatusProbeEnabled = s.StatusProbeEnabled,
    };
}
