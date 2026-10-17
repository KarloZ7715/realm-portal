using RealmPortal.Core.Servers;

namespace RealmPortal.Tests;

public class ServerTests
{
    [Fact]
    public void Create_BuildsRealmlistWithCrLfAndExtraLines()
    {
        var server = Server.Create(
            " My Server ",
            "logon.example.org",
            realm: "Realm One",
            extraRealmlistLines: ["set patchlist patch.example.org", "  ", " set foo bar "],
            requiredClientBuild: 12340,
            account: "  carlos ");

        Assert.Equal("My Server", server.Name);
        Assert.Equal("logon.example.org", server.Host);
        Assert.Equal("Realm One", server.Realm);
        Assert.Equal("carlos", server.Account);
        Assert.Equal(12340, server.RequiredClientBuild);
        Assert.Equal(
            "set realmlist logon.example.org\r\nset patchlist patch.example.org\r\nset foo bar\r\n",
            server.Realmlist);
    }

    [Fact]
    public void Create_DefaultsRealmToEmptyAndGeneratesStableLookingId()
    {
        var server = Server.Create("A", "h");

        Assert.Equal("", server.Realm);
        Assert.Null(server.Account);
        Assert.Null(server.RequiredClientBuild);
        Assert.Matches("^[0-9a-f]{32}$", server.Id);
        Assert.NotEqual(server.Id, Server.Create("A", "h").Id);
    }

    [Theory]
    [InlineData("", "h")]
    [InlineData("   ", "h")]
    [InlineData("n", "")]
    [InlineData("n", "ho st")]
    [InlineData("n", "ho\tst")]
    [InlineData("bad\nname", "h")]
    public void Create_RejectsInvalidInput(string name, string host) =>
        Assert.Throws<ArgumentException>(() => Server.Create(name, host));

    [Fact]
    public void Create_RejectsTooLongName() =>
        Assert.Throws<ArgumentException>(() => Server.Create(new string('x', Server.MaxNameLength + 1), "h"));

    [Fact]
    public void Create_RejectsExtraLineWithLineBreak() =>
        Assert.Throws<ArgumentException>(() => Server.Create("n", "h", extraRealmlistLines: ["a\r\nb"]));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_RejectsNonPositiveBuild(int build) =>
        Assert.Throws<ArgumentException>(() => Server.Create("n", "h", requiredClientBuild: build));

    [Theory]
    [InlineData("UPPER")]
    [InlineData("has space")]
    [InlineData("")]
    public void Create_RejectsInvalidId(string id) =>
        Assert.Throws<ArgumentException>(() => Server.Create("n", "h", id: id));
}
