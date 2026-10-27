namespace RealmPortal.App.Network;

/// <summary>Login-port reachability for a server card status dot (#104).</summary>
public enum LoginReachability
{
    Unknown = 0,
    Checking = 1,
    Online = 2,
    Offline = 3,
}
