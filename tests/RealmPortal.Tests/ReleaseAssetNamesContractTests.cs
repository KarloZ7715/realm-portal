using System.Diagnostics;
using RealmPortal.Core.Updates;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// The in-app updater (<see cref="ReleaseDocumentParser.AssetNameFor"/>) and the release workflow
/// (<c>packaging/release-asset-names.sh</c>) must agree on the three package names, or the updater asks for a file
/// the release does not have. C# template checks run on every OS. The script is run with a probed packaging
/// bash (Git for Windows <c>bash.exe</c> on Windows, never the WSL stub).
/// </summary>
public class ReleaseAssetNamesContractTests
{
    private static readonly InstallKind[] ScriptOrder = [InstallKind.Deb, InstallKind.AppImage, InstallKind.WindowsZip];

    private static readonly string[] StableNames =
    [
        "realm-portal_1.2.0_amd64.deb",
        "RealmPortal-1.2.0-x86_64.AppImage",
        "RealmPortal-1.2.0-win-x64.zip",
    ];

    private static readonly string[] PrereleaseNames =
    [
        "realm-portal_1.2.0-rc.1_amd64.deb",
        "RealmPortal-1.2.0-rc.1-x86_64.AppImage",
        "RealmPortal-1.2.0-rc.1-win-x64.zip",
    ];

    private static string[] UpdaterNames(ReleaseVersion version) =>
        ScriptOrder.Select(kind => ReleaseDocumentParser.AssetNameFor(kind, version)).ToArray();

    [Fact]
    public void Updater_StableVersion_UsesThePublishedAssetNameTemplate()
    {
        Assert.Equal(StableNames, UpdaterNames(new ReleaseVersion(1, 2, 0)));
    }

    [Fact]
    public void Updater_PrereleaseTemplate_SubstitutesTheFullSemVer()
    {
        // ReleaseVersion has no prerelease part; the published rc name is the 1.2.0 template with that segment swapped.
        var updater = UpdaterNames(new ReleaseVersion(1, 2, 0))
            .Select(name => name.Replace("1.2.0", "1.2.0-rc.1", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(PrereleaseNames, updater);
    }

    [PackagingBashFact]
    public void FinalVersion_MatchesTheUpdater()
    {
        Assert.Equal(UpdaterNames(new ReleaseVersion(1, 2, 0)), RunScript("1.2.0"));
    }

    [PackagingBashFact]
    public void Prerelease_UsesTheSameTemplateWithTheFullSemVer()
    {
        var script = RunScript("1.2.0-rc.1");
        var updater = UpdaterNames(new ReleaseVersion(1, 2, 0))
            .Select(name => name.Replace("1.2.0", "1.2.0-rc.1", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(updater, script);
        Assert.Equal(PrereleaseNames, script);
    }

    private static string[] RunScript(string version)
    {
        var bash = PackagingBash.Executable
            ?? throw new InvalidOperationException("Packaging bash should have skipped this test.");
        var script = Path.Combine(RepoRoot(), "packaging", "release-asset-names.sh");
        var psi = new ProcessStartInfo(bash)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(version);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start " + bash + ".");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "release-asset-names.sh timed out");
        Assert.True(
            process.ExitCode == 0,
            $"release-asset-names.sh exited {process.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{output}{Environment.NewLine}stderr:{Environment.NewLine}{error}");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RealmPortal.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
