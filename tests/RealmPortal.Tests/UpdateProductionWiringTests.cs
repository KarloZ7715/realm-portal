using System.Text.RegularExpressions;
using RealmPortal.Core.Updates;
using RealmPortal.Core.Updates.Signing;

namespace RealmPortal.Tests;

/// <summary>
/// The trusted key and the release feed URL are fixed in code (#116). Tests swap them only through constructor
/// arguments; the app itself must never pass any, and there is no setting, variable or flag that could.
/// </summary>
public class UpdateProductionWiringTests
{
    [Fact]
    public void DefaultDownloader_TrustsOnlyTheBuiltInReleaseKeys()
    {
        using var downloader = new UpdateDownloader();
        var verifier = Assert.IsType<MinisignVerifier>(downloader.SignatureVerifier);
        Assert.Equal(
            ReleaseSigningKeys.Trusted.Select(k => k.KeyId),
            verifier.TrustedKeys.Select(k => k.KeyId));
        Assert.Equal(
            MinisignVerifier.ForRelease().TrustedKeys.Select(k => k.KeyId),
            verifier.TrustedKeys.Select(k => k.KeyId));
    }

    [Fact]
    public void ReleaseFeed_IsTheHardcodedGitHubApi()
    {
        Assert.Equal(
            "https://api.github.com/repos/KarloZ7715/realm-portal/releases/latest",
            GitHubReleaseFeed.LatestReleaseUri.ToString());
        var field = typeof(GitHubReleaseFeed).GetField(nameof(GitHubReleaseFeed.LatestReleaseUri))!;
        Assert.True(field.IsInitOnly);
    }

    [Fact]
    public void App_BuildsTheUpdaterWithDefaultsOnly()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RealmPortal.App", "Portal", "PortalSession.cs"));
        Assert.Contains("new GitHubReleaseFeed()", source);
        Assert.Contains("new UpdateDownloader()", source);

        // Nothing in the shipped code passes a verifier or builds a verifier from other keys.
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(new Regex(@"signatureVerifier\s*:"), text);
            if (!file.EndsWith("MinisignVerifier.cs", StringComparison.Ordinal))
                Assert.DoesNotMatch(new Regex(@"new\s+MinisignVerifier\s*\("), text);
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
