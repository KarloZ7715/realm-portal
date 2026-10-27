using RealmPortal.Core.Service;

namespace RealmPortal.App.Network;

/// <summary>In-memory <see cref="INetworkSettingsStore"/> for tests and the demo shell.</summary>
public sealed class MemoryNetworkSettingsStore : INetworkSettingsStore
{
    private NetworkSettings _settings = NetworkSettings.Default;

    public ServiceResult<NetworkSettings> Load() => ServiceResult<NetworkSettings>.Ok(_settings);

    public ServiceResult<Unit> Save(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        return ServiceResult<Unit>.Ok(default);
    }
}
