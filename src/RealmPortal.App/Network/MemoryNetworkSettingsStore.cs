using RealmPortal.Core.Service;

namespace RealmPortal.App.Network;

/// <summary>In-memory <see cref="INetworkSettingsStore"/> for tests and the demo shell.</summary>
public sealed class MemoryNetworkSettingsStore : INetworkSettingsStore
{
    private NetworkSettings _settings = NetworkSettings.Default;

    public NetworkSettings Load() => _settings;

    public void Save(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }
}
