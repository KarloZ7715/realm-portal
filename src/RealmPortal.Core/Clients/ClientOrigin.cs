namespace RealmPortal.Core.Clients;

/// <summary>How a client installation was registered in <c>clients.json</c> (see docs/app-service.md).</summary>
public enum ClientOrigin
{
    /// <summary>Added or path-edited by the user.</summary>
    Manual = 0,

    /// <summary>Found in a standard candidate folder (<c>~/Games</c>, <c>C:\Games</c>, ...).</summary>
    Discovered = 1,

    /// <summary>Found through a Lutris game configuration.</summary>
    Lutris = 2,

    /// <summary>Found inside a Wine or Bottles prefix.</summary>
    Wine = 3,

    /// <summary>Found through a Steam library (<c>libraryfolders.vdf</c> / <c>compatdata</c>).</summary>
    Steam = 4,

    /// <summary>Alias of <see cref="Wine"/>; the wording the specification also uses for a Wine prefix.</summary>
    WinePrefix = Wine,
}
