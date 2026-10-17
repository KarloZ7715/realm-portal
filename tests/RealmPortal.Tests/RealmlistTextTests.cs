using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.Servers;

namespace RealmPortal.Tests;

public class RealmlistTextTests
{
    private const string GameFreedom =
        "set realmlist auth.gamefreedom.org\r\nset patchlist auth.gamefreedom.org\r\n";

    [Fact]
    public void Build_WithoutExtraLines_IsSingleTerminatedLine() =>
        Assert.Equal("set realmlist h\r\n", RealmlistText.Build("h"));

    [Fact]
    public void Build_RealExampleWithPatchlist_IsExact() =>
        Assert.Equal(GameFreedom, RealmlistText.Build("auth.gamefreedom.org", ["set patchlist auth.gamefreedom.org", "  "]));

    [Theory]
    [InlineData("set realmlist h\r\n")]
    [InlineData(GameFreedom)]
    [InlineData("set realmlist 10.0.0.5:3724\r\nset patchlist http://patch.example.org/files\r\n")]
    [InlineData("set realmlist [::1]:3724\r\n")]
    public void Validate_AcceptsValidText(string text) => RealmlistText.Validate(text);

    public static TheoryData<string?, InvalidRealmlistKind> Rejections => new()
    {
        { null, InvalidRealmlistKind.Empty },
        { "", InvalidRealmlistKind.Empty },
        // non-ASCII anywhere
        { "set realmlist hóst.example.org\r\n", InvalidRealmlistKind.NotAscii },
        { "set realmlist h\r\nset patchlist 龍\r\n", InvalidRealmlistKind.NotAscii },
        // wrong first line / uppercase
        { "SET REALMLIST h\r\n", InvalidRealmlistKind.WrongFirstLine },
        { "Set Realmlist h\r\n", InvalidRealmlistKind.WrongFirstLine },
        { "set patchlist h\r\nset realmlist h\r\n", InvalidRealmlistKind.WrongFirstLine },
        { " set realmlist h\r\n", InvalidRealmlistKind.WrongFirstLine },
        // other keys (including portal, not supported yet)
        { "set realmlist h\r\nset portal p.example.org\r\n", InvalidRealmlistKind.ForbiddenKey },
        { "set realmlist h\r\nset realmname Foo\r\n", InvalidRealmlistKind.ForbiddenKey },
        { "set realmlist h\r\nset PATCHLIST p\r\n", InvalidRealmlistKind.ForbiddenKey },
        // other kinds of lines
        { "set realmlist h\r\n-- comment\r\n", InvalidRealmlistKind.MalformedLine },
        { "set realmlist h\r\nSET patchlist p\r\n", InvalidRealmlistKind.MalformedLine },
        { "set realmlist h\r\nset\r\n", InvalidRealmlistKind.MalformedLine },
        { "set realmlist h\r\n\r\n", InvalidRealmlistKind.MalformedLine },
        { "set realmlist h\r\ndelete everything\r\n", InvalidRealmlistKind.MalformedLine },
        // duplicate keys
        { "set realmlist h\r\nset realmlist g\r\n", InvalidRealmlistKind.DuplicateKey },
        { "set realmlist h\r\nset patchlist a\r\nset patchlist b\r\n", InvalidRealmlistKind.DuplicateKey },
        // values: quotes, spaces, control characters, missing
        { "set realmlist \"h\"\r\n", InvalidRealmlistKind.InvalidValue },
        { "set realmlist h\r\nset patchlist 'p'\r\n", InvalidRealmlistKind.InvalidValue },
        { "set realmlist h\r\nset patchlist a b\r\n", InvalidRealmlistKind.InvalidValue },
        { "set realmlist h w\r\n", InvalidRealmlistKind.InvalidValue },
        { "set realmlist h\r\nset patchlist a\tb\r\n", InvalidRealmlistKind.InvalidValue },
        { "set realmlist h\r\nset patchlist a\u0001\r\n", InvalidRealmlistKind.InvalidValue },
        { "set realmlist \r\n", InvalidRealmlistKind.InvalidValue },
        { "set realmlist\r\n", InvalidRealmlistKind.WrongFirstLine },
        // line endings
        { "set realmlist h", InvalidRealmlistKind.BadLineEnding },
        { "set realmlist h\n", InvalidRealmlistKind.BadLineEnding },
        { "set realmlist h\r\nset patchlist p\n", InvalidRealmlistKind.BadLineEnding },
        { "set realmlist h\rset patchlist p\r\n", InvalidRealmlistKind.BadLineEnding },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void Validate_RejectsEachViolationWithItsKind(string? text, InvalidRealmlistKind kind)
    {
        var ex = Assert.Throws<InvalidRealmlistException>(() => RealmlistText.Validate(text));

        Assert.Equal(kind, ex.Kind);
        Assert.Contains($"[{kind}]", ex.Message);
        Assert.StartsWith("Invalid realmlist", ex.Message);
    }

    [Fact]
    public void Error_CarriesLineNumber_AndServerName()
    {
        var ex = Assert.Throws<InvalidRealmlistException>(() =>
            RealmlistText.Validate("set realmlist h\r\nset patchlist ok\r\nset portal x\r\n"));

        Assert.Equal(3, ex.LineNumber);
        Assert.Contains("(line 3)", ex.Message);
        Assert.Contains("not supported yet", ex.Message);

        var forServer = ex.ForServer("Mi Servidor");
        Assert.Equal("Mi Servidor", forServer.ServerName);
        Assert.Equal(ex.Kind, forServer.Kind);
        Assert.StartsWith("Server 'Mi Servidor' has an invalid realmlist (line 3)", forServer.Message);
    }

    [Theory]
    [InlineData("hóst")]
    [InlineData("h\"x")]
    [InlineData("ho st")]
    [InlineData("h\r\nset patchlist evil")]
    [InlineData("h\tx")]
    [InlineData("")]
    public void Build_RejectsBadHosts(string host) =>
        Assert.Throws<InvalidRealmlistException>(() => RealmlistText.Build(host));

    [Theory]
    [InlineData("set realmname x")]
    [InlineData("set portal x")]
    [InlineData("set patchlist ñ")]
    [InlineData("set patchlist \"x\"")]
    [InlineData("hello")]
    [InlineData("set patchlist a\r\nset patchlist b")]
    public void Build_RejectsBadExtraLines(string line) =>
        Assert.Throws<InvalidRealmlistException>(() => RealmlistText.Build("h", [line]));

    private static readonly string[] HostPool =
        ["h", "auth.gamefreedom.org", "ñ", "龍.org", "a b", "a\tb", "a\"b", "a\r\nb", "ok:3724", "😀", "", "SET", "\u0000", "~"];

    private static readonly string[] LinePool =
        ["set patchlist x", "set patchlist y.example.org", "set portal x", "set realmname z", "ñ", "龍", "a\nb", "", "  ",
         "set patchlist \"q\"", "set realmlist other", "SET patchlist x", "set patchlist a b", "-- c"];

    [Property(MaxTest = 500)]
    public Property AnythingBuildAccepts_SatisfiesTheContract() =>
        Prop.ForAll(
            Gen.Elements(HostPool).ToArbitrary(),
            Gen.Elements(LinePool).ArrayOf().ToArbitrary(),
            (host, extra) =>
            {
                string text;
                try
                {
                    text = RealmlistText.Build(host, extra);
                }
                catch (InvalidRealmlistException)
                {
                    return true; // rejected input is fine; accepted input must satisfy the contract
                }

                var lines = text[..^2].Split("\r\n");
                var keys = lines.Select(l => l.Split(' ')[1]).ToList();
                return text.StartsWith("set realmlist ", StringComparison.Ordinal)
                       && text.EndsWith("\r\n", StringComparison.Ordinal)
                       && text.All(c => c is '\r' or '\n' || (c >= ' ' && c <= '~'))
                       && !text.Contains('"') && !text.Contains('\'')
                       && lines.All(l => l.Split(' ') is ["set", _, { Length: > 0 }])
                       && keys.All(k => k is "realmlist" or "patchlist")
                       && keys.Distinct().Count() == keys.Count
                       && lines[0] == "set realmlist " + host;
            });

    [Property(MaxTest = 500)]
    public Property ValidateIsConsistentWithBuild_AndNeverThrowsOtherExceptions() =>
        Prop.ForAll(
            Gen.Elements("set realmlist h", "SET REALMLIST h", "set patchlist p", "set portal p", "ñ", "set x \"y\"", "", "a b c")
                .ArrayOf().ToArbitrary(),
            Gen.Elements("\r\n", "\n", "\r", "").ToArbitrary(),
            (lines, ending) =>
            {
                var text = string.Concat(lines.Select(l => l + ending));
                try
                {
                    RealmlistText.Validate(text);
                    return text.StartsWith("set realmlist ", StringComparison.Ordinal);
                }
                catch (InvalidRealmlistException)
                {
                    return true;
                }
            });
}
