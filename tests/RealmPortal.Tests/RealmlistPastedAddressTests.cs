using RealmPortal.Core.Servers;

namespace RealmPortal.Tests;

/// <summary>Table-driven coverage for pasted <c>set realmlist …</c> normalization (#163).</summary>
public class RealmlistPastedAddressTests
{
    public static TheoryData<string, string, bool> NormalizeCases => new()
    {
        { "logon.example.org", "logon.example.org", false },
        { "  logon.example.org:8085  ", "logon.example.org:8085", false },
        { "set realmlist logon.example.org", "logon.example.org", false },
        { "SET REALMLIST logon.example.org", "logon.example.org", false },
        { "Set Realmlist logon.example.org", "logon.example.org", false },
        { "set realmlist \"logon.example.org\"", "logon.example.org", false },
        { "set realmlist 'logon.example.org'", "logon.example.org", false },
        { "set realmlist logon.example.org;", "logon.example.org", false },
        { "set realmlist \"logon.example.org\";", "logon.example.org", false },
        { "set realmlist logon.example.org:3724", "logon.example.org:3724", false },
        { "set realmlist [::1]:8085", "[::1]:8085", false },
        {
            "set realmlist logon.example.org\r\nset patchlist patch.example.org",
            "logon.example.org",
            true
        },
        {
            "set patchlist patch.example.org\nset realmlist logon.example.org",
            "logon.example.org",
            true
        },
        { "set  realmlist   logon.example.org", "logon.example.org", false },
        // Glued "set"/"realmlist" is not a directive — token left as-is for host parse to reject.
        { "setrealmlist host.example", "setrealmlist host.example", false },
        { "set realmlistx.example", "set realmlistx.example", false },
        { "", "", false },
        { "   ", "", false },
    };

    [Theory]
    [MemberData(nameof(NormalizeCases))]
    public void NormalizePastedAddress_Table(string input, string expectedToken, bool ignoredOther)
    {
        var result = RealmlistAddress.NormalizePastedAddress(input);
        Assert.Equal(expectedToken, result.Token);
        Assert.Equal(ignoredOther, result.IgnoredOtherLines);
    }

    public static TheoryData<string, bool, string, int> ParseCases => new()
    {
        { "set realmlist logon.example.org", true, "logon.example.org", 3724 },
        { "SET REALMLIST logon.example.org:8085", true, "logon.example.org", 8085 },
        { "set realmlist \"logon.example.org\";", true, "logon.example.org", 3724 },
        { "set realmlist [::1]", true, "[::1]", 3724 },
        { "set realmlist logon.example.org\nset patchlist x", true, "logon.example.org", 3724 },
        { "set realmlist bad host", false, "", 3724 },
        { "set realmlist host;rm", false, "", 3724 },
        { "set realmlist http://evil", false, "", 3724 },
        { "set realmlist ", false, "", 3724 },
        { "setrealmlist host.example", false, "", 3724 },
        { "set realmlistx.example", false, "", 3724 },
        { "not a host line with spaces", false, "", 3724 },
    };

    [Theory]
    [MemberData(nameof(ParseCases))]
    public void TryParsePastedHostAddress_Table(string input, bool valid, string host, int port)
    {
        Assert.Equal(valid, RealmlistAddress.TryParsePastedHostAddress(input, out var parsedHost, out var parsedPort, out _));
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }
}
