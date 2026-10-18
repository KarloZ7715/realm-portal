using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientOriginTests
{
    [Fact]
    public void Values_MatchTheDiscoveryContract()
    {
        Assert.Equal(0, (int)ClientOrigin.Manual);
        Assert.Equal(1, (int)ClientOrigin.Discovered);
        Assert.Equal(2, (int)ClientOrigin.Lutris);
        Assert.Equal(3, (int)ClientOrigin.Wine);
        Assert.Equal(4, (int)ClientOrigin.Steam);
    }

    [Fact]
    public void WinePrefix_IsAnAliasOfWine()
    {
        Assert.Equal(ClientOrigin.Wine, ClientOrigin.WinePrefix);
        Assert.Equal((int)ClientOrigin.Wine, (int)ClientOrigin.WinePrefix);
    }
}
