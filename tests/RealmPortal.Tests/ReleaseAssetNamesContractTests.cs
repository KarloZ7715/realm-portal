using System.Diagnostics;
using RealmPortal.Core.Updates;

namespace RealmPortal.Tests;

/// <summary>
/// The in-app updater (<see cref="ReleaseDocumentParser.AssetNameFor"/>) and the release workflow
/// (<c>packaging/release-asset-names.sh</c>) must agree on the three package names, or the updater asks for a file
/// the release does not have. Runs the script with bash; skipped when bash is not available (some Windows hosts).
/// </summary>
public class ReleaseAssetNamesContractTests
{
    private static readonly InstallKind[] ScriptOrder = [InstallKind.Deb, InstallKind.AppImage, InstallKind.WindowsZip];

    [Fact]
    public void FinalVersion_MatchesTheUpdater()
    {
        var script = RunScript("1.2.0");
        if (script is null)
            return;

        var updater = ScriptOrder.Select(k => ReleaseDocumentParser.AssetNameFor(k, new ReleaseVersion(1, 2, 0))).ToArray();
        Assert.Equal(updater, script);
    }

    [Fact]
    public void Prerelease_UsesTheSameTemplateWithTheFullSemVer()
    {
        // ReleaseVersion has no prerelease part, so compare the templates: the updater's names for 1.2.0 with the
        // version swapped for 1.2.0-rc.1 must be exactly what the script prints for the rc.
        var script = RunScript("1.2.0-rc.1");
        if (script is null)
            return;

        var updater = ScriptOrder
            .Select(k => ReleaseDocumentParser.AssetNameFor(k, new ReleaseVersion(1, 2, 0)).Replace("1.2.0", "1.2.0-rc.1", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(updater, script);
        Assert.Equal(["realm-portal_1.2.0-rc.1_amd64.deb", "RealmPortal-1.2.0-rc.1-x86_64.AppImage", "RealmPortal-1.2.0-rc.1-win-x64.zip"], script);
    }

    private static string[]? RunScript(string version)
    {
        var script = Path.Combine(RepoRoot(), "packaging", "release-asset-names.sh");
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(version);

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception) when (OperatingSystem.IsWindows())
        {
            return null; // no bash on this Windows host
        }

        using (process!)
        {
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(30_000), "release-asset-names.sh timed out");
            if (OperatingSystem.IsWindows() && process.ExitCode != 0 && output.Length == 0)
                return null; // e.g. the WSL bash stub with no distribution
            Assert.True(process.ExitCode == 0, error);
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
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
