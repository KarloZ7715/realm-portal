using System.Text.Json;

namespace RealmPortal.Tests;

/// <summary>
/// Every NuGet package the app ships must have its license files committed under licenses/nuget/ (see
/// packaging/collect-licenses.sh, which enforces the same rule against the real publish deps.json).
/// </summary>
public class NuGetLicenseManifestTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void EveryAppPackageInTheLockFile_HasALicenseRow()
    {
        var rows = ReadManifest();
        var missing = AppLockPackages()
            .Where(p => !rows.ContainsKey(p))
            .ToList();
        Assert.True(missing.Count == 0, "Add licenses/nuget rows for: " + string.Join(", ", missing));
    }

    [Fact]
    public void ManifestRows_AreNotStale()
    {
        var shipped = AppLockPackages().ToHashSet(StringComparer.Ordinal);
        var stale = ReadManifest().Keys.Where(k => !shipped.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Rows for packages the app no longer uses: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryLicenseFolder_ExistsAndHasNonEmptyFiles()
    {
        foreach (var (key, folder) in ReadManifest())
        {
            var dir = Path.Combine(Root, "licenses", "nuget", folder);
            Assert.True(Directory.Exists(dir), $"{key}: missing licenses/nuget/{folder}");
            var files = Directory.GetFiles(dir);
            Assert.NotEmpty(files);
            Assert.All(files, f => Assert.True(new FileInfo(f).Length > 0, $"{key}: empty {f}"));
        }
    }

    [Fact]
    public void ThirdPartyLicenses_ListsEveryShippedPackage()
    {
        var text = File.ReadAllText(Path.Combine(Root, "THIRD_PARTY_LICENSES.md"));
        var unlisted = ReadManifest().Keys
            .Select(k => k.Split('/'))
            .Where(p => !text.Contains($"| {p[0]} | {p[1]} |", StringComparison.Ordinal))
            .Select(p => $"{p[0]} {p[1]}")
            .ToList();
        Assert.True(unlisted.Count == 0, "THIRD_PARTY_LICENSES.md is missing rows for: " + string.Join(", ", unlisted));
    }

    /// <summary>"Id/Version" keys mapped to their license folder.</summary>
    private static Dictionary<string, string> ReadManifest()
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(Root, "licenses", "nuget", "packages.tsv")))
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var cols = line.Split('\t');
            Assert.True(cols.Length >= 5, $"Malformed row: {line}");
            Assert.True(rows.TryAdd($"{cols[0]}/{cols[1]}", cols[3]), $"Duplicate row: {line}");
        }
        return rows;
    }

    private static IEnumerable<string> AppLockPackages()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "RealmPortal.App", "packages.lock.json")));
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var target in doc.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var dep in target.Value.EnumerateObject())
            {
                if (dep.Value.GetProperty("type").GetString() == "Project")
                    continue;
                set.Add($"{dep.Name}/{dep.Value.GetProperty("resolved").GetString()}");
            }
        }
        return set;
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RealmPortal.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
