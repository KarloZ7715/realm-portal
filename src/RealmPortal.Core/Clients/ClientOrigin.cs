namespace RealmPortal.Core.Clients;

/// <summary>How a registered client folder was found.</summary>
public enum ClientOrigin
{
    Manual = 0,
    Discovered = 1,
    Lutris = 2,
    Wine = 3,
    Steam = 4,
    WinePrefix = Wine,
}
