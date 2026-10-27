using RealmPortal.Core.Service;

namespace RealmPortal.App.Network;

/// <summary>
/// App access to <c>network-settings.json</c> via Core's <see cref="NetworkSettings"/> model
/// (Service/NetworkSettingsStore behind <see cref="IPortalService"/>).
/// </summary>
public interface INetworkSettingsStore
{
    NetworkSettings Load();

    void Save(NetworkSettings settings);
}
