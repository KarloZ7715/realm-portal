namespace RealmPortal.App.Network;

/// <summary>Persists <see cref="NetworkSettings"/> to <c>network-settings.json</c> in the config directory.</summary>
public interface INetworkSettingsStore
{
    NetworkSettings Load();

    void Save(NetworkSettings settings);
}
