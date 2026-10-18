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

    [Theory]
    [InlineData("[:::]")]
    [InlineData("[1]")]
    public void Create_RejectsInvalidBracketedIpLiterals(string host) =>
        Assert.Throws<InvalidRealmlistException>(() => Server.Create("n", host));

    [Theory]
    [InlineData("http://$(id)")]
    [InlineData("http://a;calc")]
    [InlineData("http://user:pass@patch.example.org/")]
    [InlineData("http://patch.example.org:99999/")]
    [InlineData("http://patch.example.org/?y=$(id)")]
    public void Create_RejectsUnsafePatchlistUrls(string patchlistUrl) =>
        Assert.Throws<InvalidRealmlistException>(() =>
            Server.Create("n", "h.example.org", extraRealmlistLines: ["set patchlist " + patchlistUrl]));

    [Fact]
    public void TryParseHostAddress_RejectsShellMetacharactersInHost() =>
        Assert.False(RealmlistAddress.TryParseHostAddress("a.example;calc", out _, out _));

    [Theory]
    [InlineData("fe80::1%$(id)")]
    [InlineData("[fe80::1%$(id)]")]
    [InlineData("[fe80::1%eth0]:3724")]
    [InlineData("fe80::1%25eth0")]
    public void Create_RejectsIpv6ZoneIds(string host) =>
        Assert.Throws<InvalidRealmlistException>(() => Server.Create("n", host));

    [Theory]
    [InlineData("127.1")]
    [InlineData("2130706433")]
    [InlineData("0x7f000001")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("0177.0.0.1")]
    [InlineData("127.000.000.001")]
    [InlineData("1.2.3.4.5")]
    [InlineData("256.0.0.1")]
    [InlineData("127.0.0.1.")]
    public void Create_RejectsNonCanonicalIpv4(string host) =>
        Assert.Throws<InvalidRealmlistException>(() => Server.Create("n", host));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.0.2.10:8085")]
    [InlineData("123.example.org")]
    [InlineData("::1")]
    [InlineData("[2001:db8::1]")]
    public void Create_AcceptsCanonicalAddresses(string host) =>
        Server.Create("n", host);

    [Theory]
    [InlineData("http://patch.example.org/#$(id)")]
    [InlineData("http://patch.example.org/#;id")]
    [InlineData("http://patch.example.org/a`id`")]
    [InlineData("http://patch.example.org/a|id")]
    [InlineData("http://patch.example.org/a<b")]
    [InlineData("http://patch.example.org/?a=1&b=2")]
    [InlineData("http://patch.example.org/#frag")]
    [InlineData("http://patch.example.org/a%20b c")]
    [InlineData("http://2130706433/")]
    [InlineData("http://127.1/")]
    [InlineData("http://[fe80::1%25eth0]/")]
    public void Create_RejectsUnsafePatchlistUrlsAsWritten(string patchlistUrl) =>
        Assert.Throws<InvalidRealmlistException>(() =>
            Server.Create("n", "h.example.org", extraRealmlistLines: ["set patchlist " + patchlistUrl]));

    [Theory]
    [InlineData("http://patch.example.org/files")]
    [InlineData("https://patch.example.org:8443/wow/patch.mpq?v=3")]
    [InlineData("http://192.0.2.10/")]
    [InlineData("http://[2001:db8::1]:8080/")]
    public void Create_AcceptsPlainPatchlistUrls(string patchlistUrl) =>
        Server.Create("n", "h.example.org", extraRealmlistLines: ["set patchlist " + patchlistUrl]);

    [Theory]
    [InlineData("[[::1]]")]
    [InlineData("[[::1]]:3724")]
    [InlineData("[::1]]")]
    [InlineData("[::1")]
    public void NestedOrUnbalancedBrackets_AreInvalidIpv6(string host)
    {
        var ex = Assert.Throws<InvalidRealmlistException>(() => Server.Create("n", host));
        Assert.Contains("IPv6", ex.Message);
        Assert.False(RealmlistAddress.TryParseHostAddress(host, out _, out _));
    }
}
