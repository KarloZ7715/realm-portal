using RealmPortal.Core.Service;

namespace RealmPortal.App.Network;

/// <summary>
/// App access to <c>network-settings.json</c> via Core's <see cref="NetworkSettings"/> model
/// (Service/NetworkSettingsStore behind <see cref="IPortalService"/>).
/// Failures are returned as <see cref="ServiceResult{T}"/> — callers must not invent defaults silently.
/// Field tweaks must use <see cref="Update"/> under lock — never Load + Save of a stale full snapshot.
/// </summary>
public interface INetworkSettingsStore
{
    ServiceResult<NetworkSettings> Load();

    ServiceResult<Unit> Save(NetworkSettings settings);

    /// <summary>Read-modify-write under the config lock (preserves concurrent etag / last_attempt / probe).</summary>
    ServiceResult<NetworkSettings> Update(Func<NetworkSettings, NetworkSettings> mutate);
}
