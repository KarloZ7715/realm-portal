using RealmPortal.Core.Servers;

namespace RealmPortal.Tests;

public class RealmlistHostValidationTests
{
    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("ftp://u:p@evil.example/x")]
    [InlineData("host.example:99999")]
    [InlineData(":0")]
    [InlineData(":-1")]
    [InlineData(":abc")]
    [InlineData("host:1:2:3")]
    [InlineData("::1")]
    [InlineData("a.example;calc")]
    [InlineData("$(id)`id`")]
    [InlineData("a..example")]
    [InlineData("-")]
    public void Create_RejectsUnsafeOrMalformedHosts(string host) =>
        Assert.Throws<InvalidRealmlistException>(() => Server.Create("n", host));

    [Fact]
    public void Create_RejectsVeryLongHost() =>
        Assert.Throws<InvalidRealmlistException>(() => Server.Create("n", new string('a', 100_000)));

    [Fact]
    public void Create_RejectsVeryLongPatchlistValue()
    {
        var line = "set patchlist " + new string('a', 1_000_000);
        Assert.Throws<InvalidRealmlistException>(() => Server.Create("n", "h.example.org", extraRealmlistLines: [line]));
    }

    [Theory]
    [InlineData("logon.example.org")]
    [InlineData("10.0.0.5:3724")]
    [InlineData("[::1]:3724")]
    public void Create_AcceptsValidHosts(string host) =>
        Server.Create("n", host);

    [Fact]
    public void Validate_AcceptsPatchlistHttpUrl() =>
        RealmlistText.Validate("set realmlist h.example.org\r\nset patchlist http://patch.example.org/files\r\n");
}
