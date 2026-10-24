using RealmPortal.Core.Clients;
using RealmPortal.Core.Service;

namespace RealmPortal.App.Localization;

/// <summary>
/// The one place that turns a client version into UI text, so every screen shows the same thing:
/// Core's <see cref="ClientVersion.DisplayVersion"/> ("3.3.5a (12340)") or a localized "Unknown version".
/// </summary>
public static class VersionText
{
    /// <summary>The version, or "Unknown version" when it could not be read.</summary>
    public static string Of(ClientVersion? version) =>
        version?.DisplayVersion ?? Loc.Get("clients.version.unknown");

    /// <summary>
    /// The client's version; when it could not be read, the localized title of the reason (e.g. "Not a Windows executable"),
    /// or "Unknown version" when there is no reason.
    /// </summary>
    public static string Of(ClientEntry client) =>
        client.Version is { } version
            ? version.DisplayVersion
            : client.Error is { } error ? ErrorText.Title(error.Code.ToString()) : Of((ClientVersion?)null);

    /// <summary>"Client 3.3.5a (12340)" for a client without a name, or "Unknown client version".</summary>
    public static string ClientTitle(ClientVersion? version) =>
        version is null ? Loc.Get("clients.client.unknown") : Loc.Format("clients.client", version.DisplayVersion);

    /// <summary>"Name · version" for a named client, so the version is shown once.</summary>
    public static string WithName(string name, string version) => $"{name} · {version}";
}
