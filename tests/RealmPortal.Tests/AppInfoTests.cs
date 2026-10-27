using System.Reflection;
using System.Xml.Linq;
using RealmPortal.Cli;
using RealmPortal.Core;
using RealmPortal.Core.Clients;

namespace RealmPortal.Tests;

public class AppInfoTests
{
    [Fact]
    public void Name_IsRealmPortal() => Assert.Equal("Realm Portal", AppInfo.Name);

    [Fact]
    public void RepositoryUrl_IsThePublicGitHubRepo() =>
        Assert.Equal("https://github.com/KarloZ7715/realm-portal", AppInfo.RepositoryUrl);

    [Fact]
    public void Version_IsParseableAndHasNoBuildMetadata()
    {
        Assert.DoesNotContain('+', AppInfo.Version);
        Assert.True(Version.TryParse(AppInfo.Version.Split('-')[0], out _), $"Not a version: {AppInfo.Version}");
    }

    [Fact]
    public void Version_IsTheAssemblyInformationalVersionWithoutBuildMetadata()
    {
        var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(info.Split('+')[0], AppInfo.Version);
        var assemblyVersion = typeof(AppInfo).Assembly.GetName().Version!;
        Assert.Equal($"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}", AppInfo.Version.Split('-')[0]);
    }

    /// <summary>Source builds report the version declared once in Directory.Build.props (issue #102).</summary>
    [Fact]
    public void Version_MatchesDirectoryBuildProps() =>
        Assert.Equal(ReadPropsVersion(), AppInfo.Version);

    [Fact]
    public void CliVersion_PrintsAppInfoVersion()
    {
        var output = new StringWriter();
        var code = Program.Run(["--version"], output, new StringWriter(), new PeClientVersionReader());
        Assert.Equal(0, code);
        Assert.Equal($"{AppInfo.Name} {ReadPropsVersion()}", output.ToString().Trim());
    }

    [Fact]
    public void PackagingFiles_DoNotHardcodeTheVersion()
    {
        var root = RepoRoot();
        var metainfo = File.ReadAllText(Path.Combine(root, "packaging", "linux", "com.karloz7715.RealmPortal.metainfo.xml"));
        Assert.DoesNotContain("<releases", metainfo, StringComparison.Ordinal);
        Assert.DoesNotContain("<release ", metainfo, StringComparison.Ordinal);
    }

    private static string ReadPropsVersion()
    {
        var props = XDocument.Load(Path.Combine(RepoRoot(), "Directory.Build.props"));
        var version = props.Descendants("Version").Single().Value.Trim();
        Assert.False(string.IsNullOrEmpty(version));
        return version;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")) && File.Exists(Path.Combine(dir.FullName, "RealmPortal.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Repository root (Directory.Build.props) not found above " + AppContext.BaseDirectory);
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
