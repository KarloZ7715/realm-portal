using RealmPortal.Core.Service;

namespace RealmPortal.App.Network;

/// <summary>
/// App access to <c>network-settings.json</c> via Core's <see cref="NetworkSettings"/> model
/// (Service/NetworkSettingsStore behind <see cref="IPortalService"/>).
/// Failures are returned as <see cref="ServiceResult{T}"/> — callers must not invent defaults silently.
/// </summary>
public interface INetworkSettingsStore
{
    ServiceResult<NetworkSettings> Load();

    ServiceResult<Unit> Save(NetworkSettings settings);
}
