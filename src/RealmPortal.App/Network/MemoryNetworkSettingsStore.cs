using RealmPortal.Core.Service;

namespace RealmPortal.App.Network;

/// <summary>In-memory <see cref="INetworkSettingsStore"/> for tests and the demo shell.</summary>
public sealed class MemoryNetworkSettingsStore : INetworkSettingsStore
{
    private readonly object _gate = new();
    private NetworkSettings _settings = NetworkSettings.Default;

    public ServiceResult<NetworkSettings> Load()
    {
        lock (_gate)
            return ServiceResult<NetworkSettings>.Ok(_settings);
    }

    public ServiceResult<Unit> Save(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
            _settings = settings;
        return ServiceResult<Unit>.Ok(default);
    }

    public ServiceResult<NetworkSettings> Update(Func<NetworkSettings, NetworkSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        lock (_gate)
        {
            _settings = mutate(_settings) ?? throw new InvalidOperationException("mutate returned null.");
            return ServiceResult<NetworkSettings>.Ok(_settings);
        }
    }
}
