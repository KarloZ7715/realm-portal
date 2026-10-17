using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.Servers;

namespace RealmPortal.Tests;

public class RealmlistTextTests
{
    [Fact]
    public void Build_WithoutExtraLines_IsSingleTerminatedLine() =>
        Assert.Equal("set realmlist h\r\n", RealmlistText.Build("h"));

    [Property]
    public Property Build_EveryLineEndsWithCrLf_AndNoBareLineFeeds() =>
        Prop.ForAll(
            Gen.Elements("set patchlist a.example.org", "  set x y  ", "", "   ", "-- comment").ArrayOf().ToArbitrary(),
            extra =>
            {
                var text = RealmlistText.Build("host.example.org", extra);
                var withoutCrLf = text.Replace("\r\n", "");
                return text.EndsWith("\r\n", StringComparison.Ordinal)
                       && !withoutCrLf.Contains('\n')
                       && !withoutCrLf.Contains('\r')
                       && text.StartsWith("set realmlist host.example.org\r\n", StringComparison.Ordinal);
            });
}

public class RealmlistTextValidationTests
{
    [Theory]
    [InlineData("set realmlist h\r\n")]
    [InlineData("set realmlist auth.gamefreedom.org\r\nset patchlist auth.gamefreedom.org\r\n")]
    [InlineData("set realmlist 10.0.0.5:3724\r\nset x y\r\n")]
    public void Validate_AcceptsCanonicalText(string text) => Assert.Null(RealmlistText.Validate(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SET REALMLIST h\r\n")]
    [InlineData("Set Realmlist h\r\n")]
    [InlineData(" set realmlist h\r\n")]
    [InlineData("set realmlist\r\n")]
    [InlineData("set realmlist \r\n")]
    [InlineData("set realmlist h")]
    [InlineData("set realmlist h\n")]
    [InlineData("set realmlist h\r\nset patchlist p\n")]
    [InlineData("set realmlist h\rset patchlist p\r\n")]
    [InlineData("set realmlist hóst\r\n")]
    [InlineData("set realmlist h\r\nset x 龍\r\n")]
    [InlineData("set realmlist h\r\nset x\ty\r\n")]
    public void Validate_RejectsViolations(string? text) => Assert.NotNull(RealmlistText.Validate(text));

    [Fact]
    public void Validate_ReasonMentionsTheProblem()
    {
        Assert.Contains("lowercase", RealmlistText.Validate("SET REALMLIST h\r\n"));
        Assert.Contains("non-ASCII", RealmlistText.Validate("set realmlist hóst\r\n"));
        Assert.Contains("CR LF", RealmlistText.Validate("set realmlist h"));
    }

    [Fact]
    public void Build_RejectsNonAsciiHost() =>
        Assert.Throws<ArgumentException>(() => RealmlistText.Build("hóst"));

    [Property]
    public Property AnythingBuildAccepts_IsValid() =>
        Prop.ForAll(
            Gen.Elements("a", "Z", "é", "龍", " ", "\t", "\r", "\n", ".", "-", ":", "7", "😀", "\u0000", "~")
                .ArrayOf().Select(c => string.Concat(c)).ToArbitrary(),
            Gen.Elements("set x y", "ñ", "", "  ", "a\r\nb", "-- 龍", "ok")
                .ArrayOf().ToArbitrary(),
            (host, extra) =>
            {
                string text;
                try
                {
                    text = RealmlistText.Build(host, extra);
                }
                catch (ArgumentException)
                {
                    return true; // rejected input is fine; accepted input must satisfy the contract
                }

                return RealmlistText.Validate(text) is null
                       && text.StartsWith("set realmlist ", StringComparison.Ordinal)
                       && text.All(c => c is '\r' or '\n' || RealmlistText.IsPrintableAscii(c));
            });
}
