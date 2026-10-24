using FsCheck;
using FsCheck.Xunit;
using RealmPortal.Core.Clients;

namespace RealmPortal.Tests;

public class ClientVersionTests
{
    [Fact]
    public void Build_IsLastComponentOfFileVersion() =>
        Assert.Equal(12340, new ClientVersion(new Version(3, 3, 5, 12340)).Build);

    [Theory]
    [InlineData("1.12.1.5875", "1.12.1", "1.12.1 (5875)")]
    [InlineData("2.4.3.8606", "2.4.3", "2.4.3 (8606)")]
    [InlineData("3.3.5.12340", "3.3.5a", "3.3.5a (12340)")]
    [InlineData("4.3.4.15595", "4.3.4", "4.3.4 (15595)")]
    [InlineData("5.4.8.18414", "5.4.8", "5.4.8 (18414)")]
    public void KnownBuild_UsesPatchLabelAndBuild(string fileVersion, string patch, string display)
    {
        var version = new ClientVersion(Version.Parse(fileVersion));

        Assert.True(version.IsKnownBuild);
        Assert.Equal(patch, version.PatchLabel);
        Assert.Equal(display, version.DisplayVersion);
        Assert.Equal(display, version.ToString());
    }

    [Theory]
    [InlineData("3.3.5.12345", "3.3.5", "3.3.5 (12345)")]
    [InlineData("6.2.4.20779", "6.2.4", "6.2.4 (20779)")]
    [InlineData("1.12.0.5595", "1.12.0", "1.12.0 (5595)")]
    public void UnknownBuild_FallsBackToFileVersion(string fileVersion, string patch, string display)
    {
        var version = new ClientVersion(Version.Parse(fileVersion));

        Assert.False(version.IsKnownBuild);
        Assert.Equal(patch, version.PatchLabel);
        Assert.Equal(display, version.DisplayVersion);
    }

    [Theory]
    [InlineData("3.3.5.0", "3.3.5")]
    [InlineData("3.3.5", "3.3.5")]
    [InlineData("3.3", "3.3.0")]
    public void MissingBuild_ShowsPatchLabelOnly(string fileVersion, string display)
    {
        var version = new ClientVersion(Version.Parse(fileVersion));

        Assert.False(version.HasBuild);
        Assert.False(version.IsKnownBuild);
        Assert.Equal(display, version.PatchLabel);
        Assert.Equal(display, version.DisplayVersion);
    }

    [Fact]
    public void KnownBuild_DependsOnlyOnTheBuildNumber() =>
        Assert.Equal("3.3.5a (12340)", new ClientVersion(new Version(3, 3, 0, 12340)).DisplayVersion);

    [Fact]
    public void KnownClientBuilds_TableMatchesSupportedLayouts()
    {
        Assert.Equal(
            new Dictionary<int, string> { [5875] = "1.12.1", [8606] = "2.4.3", [12340] = "3.3.5a", [15595] = "4.3.4", [18414] = "5.4.8" },
            KnownClientBuilds.All.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value));
        Assert.False(KnownClientBuilds.TryGetPatchLabel(12345, out var label));
        Assert.Equal(string.Empty, label);
    }

    [Property]
    public void DisplayVersion_AlwaysEndsWithBuild_WhenBuildIsPositive(PositiveInt build, byte major, byte minor, byte patch)
    {
        var version = new ClientVersion(new Version(major, minor, patch, build.Get));

        Assert.EndsWith($" ({build.Get})", version.DisplayVersion);
        Assert.Equal($"{version.PatchLabel} ({build.Get})", version.DisplayVersion);
        if (!version.IsKnownBuild)
            Assert.Equal($"{major}.{minor}.{patch}", version.PatchLabel);
    }

    [Fact]
    public void Exception_ExposesKindAndPath()
    {
        var ex = new ClientVersionException(ClientVersionErrorKind.NotAPortableExecutable, "/x/Wow.exe", "bad");
        Assert.Equal(ClientVersionErrorKind.NotAPortableExecutable, ex.Kind);
        Assert.Equal("/x/Wow.exe", ex.Path);
        Assert.Equal("bad", ex.Message);
    }
}
