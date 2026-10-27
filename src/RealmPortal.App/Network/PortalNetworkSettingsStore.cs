using RealmPortal.Core.Service;

namespace RealmPortal.App.Network;

/// <summary>
/// Forwards to Core <c>NetworkSettingsStore</c> through <see cref="IPortalService"/>
/// (<c>GetNetworkSettings</c> / <c>SetNetworkSettings</c> / <c>UpdateNetworkSettings</c>).
/// Prefer this over any App-local JSON writer.
/// </summary>
public sealed class PortalNetworkSettingsStore(IPortalService service) : INetworkSettingsStore
{
    private readonly IPortalService _service = service ?? throw new ArgumentNullException(nameof(service));

    public ServiceResult<NetworkSettings> Load() => _service.GetNetworkSettings();

    public ServiceResult<Unit> Save(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return _service.SetNetworkSettings(settings);
    }

    public ServiceResult<NetworkSettings> Update(Func<NetworkSettings, NetworkSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        return _service.UpdateNetworkSettings(mutate);
    }
}
