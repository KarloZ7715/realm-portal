using RealmPortal.Core;

namespace RealmPortal.Tests;

public class AppInfoTests
{
    [Fact]
    public void Name_IsRealmPortal() => Assert.Equal("Realm Portal", AppInfo.Name);

    [Fact]
    public void Version_IsParseableAndHasNoBuildMetadata()
    {
        Assert.DoesNotContain('+', AppInfo.Version);
        Assert.True(Version.TryParse(AppInfo.Version.Split('-')[0], out _), $"Not a version: {AppInfo.Version}");
    }

    [Theory]
    [InlineData("es", AppInfo.DisclaimerEs)]
    [InlineData("es-CO", AppInfo.DisclaimerEs)]
    [InlineData("ES-mx", AppInfo.DisclaimerEs)]
    [InlineData("en-US", AppInfo.DisclaimerEn)]
    [InlineData("fr", AppInfo.DisclaimerEn)]
    [InlineData(null, AppInfo.DisclaimerEn)]
    public void GetDisclaimer_PicksLanguage(string? culture, string expected) =>
        Assert.Equal(expected, AppInfo.GetDisclaimer(culture));

    [Fact]
    public void Disclaimers_MentionBlizzard()
    {
        Assert.Contains("Blizzard Entertainment", AppInfo.DisclaimerEs);
        Assert.Contains("Blizzard Entertainment", AppInfo.DisclaimerEn);
    }
}
