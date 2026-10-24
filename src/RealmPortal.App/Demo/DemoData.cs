using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Demo;

/// <summary>
/// Made-up servers and clients for the F3b screens. Names, hosts and paths are examples only
/// (".example" domains and "/home/demo"): nothing here reads or writes a real client or configuration.
/// </summary>
public static class DemoData
{
    // Same formatter as real clients (Core's ClientVersion.DisplayVersion): "3.3.5a (12340)", "1.12.1 (5875)".
    public static readonly string Wotlk = new RealmPortal.Core.Clients.ClientVersion(new Version(3, 3, 5, 12340)).DisplayVersion;
    public static readonly string Classic = new RealmPortal.Core.Clients.ClientVersion(new Version(1, 12, 1, 5875)).DisplayVersion;

    public static IReadOnlyList<ServerSeed> Servers { get; } =
    [
        new("Mi Servidor", "logon.ejemplo.com", 3724, Wotlk, "demo", IsActive: true, IsCompatible: true),
        new("Otro Servidor", "play.otro.net", 3724, Wotlk, null, IsActive: false, IsCompatible: true),
        new("Servidor Clásico", "classic.ejemplo.org", 3724, Classic, null, IsActive: false, IsCompatible: false),
        new("Otro Reino RP", "otro.reino.net", 3724, Wotlk, "demo", IsActive: false, IsCompatible: true),
    ];

    public static IReadOnlyList<ClientSeed> Clients { get; } =
    [
        new(Wotlk, "/home/demo/Games/Client-335", ClientOrigin.Lutris, InUse: true),
        new(Classic, "/home/demo/.wine/drive_c/WoW-Classic", ClientOrigin.Wine, InUse: true),
        new(Wotlk, "/home/demo/.steam/steamapps/common/WoW", ClientOrigin.Steam, InUse: false),
    ];

    /// <summary>Folders the simulated "pick a folder by hand" button adds, one per press.</summary>
    public static IReadOnlyList<ClientSeed> PickableClients { get; } =
    [
        new(Wotlk, "/home/demo/Games/Client-335-copy", ClientOrigin.Manual, InUse: false),
        new(Classic, "/home/demo/Games/Vanilla", ClientOrigin.Manual, InUse: false),
    ];
}

public sealed record ServerSeed(string Name, string Host, int Port, string ClientVersion, string? Account, bool IsActive, bool IsCompatible);

public enum ClientOrigin
{
    Lutris,
    Wine,
    Steam,
    Manual,
}

public sealed record ClientSeed(string Version, string Path, ClientOrigin Origin, bool InUse);
