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
