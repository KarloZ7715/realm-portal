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
            extraRealmlistLines: ["set patchlist patch.example.org", "  "],
            requiredClientBuild: 12340,
            account: "  player ");

        Assert.Equal("My Server", server.Name);
        Assert.Equal("logon.example.org", server.Host);
        Assert.Equal("Realm One", server.Realm);
        Assert.Equal("player", server.Account);
        Assert.Equal(12340, server.RequiredClientBuild);
        Assert.Equal(
            "set realmlist logon.example.org\r\nset patchlist patch.example.org\r\n",
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
    [InlineData("bad\nname", "h")]
    public void Create_RejectsInvalidInput(string name, string host) =>
        Assert.Throws<ArgumentException>(() => Server.Create(name, host));

    [Theory]
    [InlineData("Tag\U000E0001Name")] // U+E0001 LANGUAGE TAG: a Format character outside the BMP (a surrogate pair)
    [InlineData("Zero\u200BWidth")]    // U+200B ZERO WIDTH SPACE: a Format character inside the BMP
    public void Create_RejectsFormatCharactersInMetadata(string value)
    {
        Assert.Throws<ArgumentException>(() => Server.Create(value, "h"));
        Assert.Throws<ArgumentException>(() => Server.Create("n", "h", realm: value));
        Assert.Throws<ArgumentException>(() => Server.Create("n", "h", account: value));
    }

    [Theory]
    [InlineData("ho st", InvalidRealmlistKind.InvalidValue)]
    [InlineData("ho\tst", InvalidRealmlistKind.InvalidValue)]
    [InlineData("ho\"st", InvalidRealmlistKind.InvalidValue)]
    [InlineData("ünï.example.org", InvalidRealmlistKind.NotAscii)]
    [InlineData("host.例え.jp", InvalidRealmlistKind.NotAscii)]
    public void Create_RejectsHostsThatBreakTheRealmlistContract(string host, InvalidRealmlistKind kind) =>
        Assert.Equal(kind, Assert.Throws<InvalidRealmlistException>(() => Server.Create("n", host)).Kind);

    [Fact]
    public void Create_RejectsTooLongName() =>
        Assert.Throws<ArgumentException>(() => Server.Create(new string('x', Server.MaxNameLength + 1), "h"));

    [Fact]
    public void Create_AcceptsExplicitValidId() =>
        Assert.Equal("0123456789abcdef0123456789abcdef", Server.Create("n", "h", id: "0123456789abcdef0123456789abcdef").Id);

    [Theory]
    [InlineData("set patchlist pätch.example.org")]
    [InlineData("set portal p.example.org")]
    [InlineData("set realmname x")]
    [InlineData("-- comment")]
    [InlineData("set patchlist \"x\"")]
    [InlineData("set patchlist a b")]
    public void Create_RejectsExtraLinesThatBreakTheRealmlistContract(string line) =>
        Assert.Throws<InvalidRealmlistException>(() => Server.Create("n", "h", extraRealmlistLines: [line]));

    [Fact]
    public void Create_AllowsNonAsciiNameAndRealm_ButRealmlistStaysAscii()
    {
        var server = Server.Create("Ñandú 龍", "h.example.org", "Reino ñ");

        Assert.Equal("Ñandú 龍", server.Name);
        RealmlistText.Validate(server.Realmlist);
        Assert.StartsWith("set realmlist ", server.Realmlist, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RejectsExtraLineWithLineBreak() =>
        Assert.Throws<InvalidRealmlistException>(() => Server.Create("n", "h", extraRealmlistLines: ["a\r\nb"]));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_RejectsNonPositiveBuild(int build) =>
        Assert.Throws<ArgumentException>(() => Server.Create("n", "h", requiredClientBuild: build));

    [Theory]
    [InlineData("UPPER")]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("0123456789abcdef-123456789abcdef")]
    public void Create_RejectsInvalidId(string id) =>
        Assert.Throws<ArgumentException>(() => Server.Create("n", "h", id: id));
}
