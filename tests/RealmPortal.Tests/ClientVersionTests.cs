using RealmPortal.Core.Clients;

namespace RealmPortal.Tests;

public class ClientVersionTests
{
    [Fact]
    public void Build_IsLastComponentOfFileVersion() =>
        Assert.Equal(12340, new ClientVersion(new Version(3, 3, 5, 12340)).Build);

    [Fact]
    public void ToString_FormatsBuildAndFileVersion() =>
        Assert.Equal("12340 (3.3.5.12340)", new ClientVersion(new Version(3, 3, 5, 12340)).ToString());

    [Fact]
    public void Exception_ExposesKindAndPath()
    {
        var ex = new ClientVersionException(ClientVersionErrorKind.NotAPortableExecutable, "/x/Wow.exe", "bad");
        Assert.Equal(ClientVersionErrorKind.NotAPortableExecutable, ex.Kind);
        Assert.Equal("/x/Wow.exe", ex.Path);
        Assert.Equal("bad", ex.Message);
    }
}
