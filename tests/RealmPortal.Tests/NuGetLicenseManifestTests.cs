using System.Text.Json;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// Every NuGet package the app ships must have its listed license files committed under licenses/nuget/ (see
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
    public void EveryLicenseFolder_HasExactlyTheListedNonEmptyFiles()
    {
        var nuget = Path.Combine(Root, "licenses", "nuget");
        var problems = ReadRows().Values
            .Select(r => (r.Folder, r.Files))
            .Distinct()
            .SelectMany(r => FolderProblems(nuget, r.Folder, r.Files))
            .ToList();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void RowsSharingAFolder_ListTheSameFiles()
    {
        var conflicts = ReadRows().Values
            .GroupBy(r => r.Folder)
            .Where(g => g.Select(r => string.Join(",", r.Files)).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(conflicts.Count == 0, "Rows list different files for: " + string.Join(", ", conflicts));
    }

    [Theory]
    [InlineData("penet.asn1-2.0.1", "LICENSE-Apache-2.0.txt")]
    [InlineData("skiasharp-2.88.9", "THIRD-PARTY-NOTICES.txt")]
    [InlineData("avalonia-11.3.22", "NOTICE.md")]
    public void FolderProblems_ReportsASingleDeletedOrEmptiedFile(string folder, string file)
    {
        var row = ReadRows().Values.First(r => r.Folder == folder);
        Assert.Contains(file, row.Files);
        using var dir = new TempDirectory();
        var copy = dir.Combine(folder);
        Directory.CreateDirectory(copy);
        foreach (var f in Directory.GetFiles(Path.Combine(Root, "licenses", "nuget", folder)))
            File.Copy(f, Path.Combine(copy, Path.GetFileName(f)));
        Assert.Empty(FolderProblems(dir.Path, folder, row.Files));

        File.Delete(Path.Combine(copy, file));
        Assert.Equal([$"licenses/nuget/{folder}/{file} missing or empty"], FolderProblems(dir.Path, folder, row.Files));

        File.WriteAllText(Path.Combine(copy, file), "");
        Assert.Equal([$"licenses/nuget/{folder}/{file} missing or empty"], FolderProblems(dir.Path, folder, row.Files));
    }

    [Fact]
    public void FolderProblems_ReportsAnUnlistedFile()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Combine("pkg-1.0.0"));
        File.WriteAllText(dir.Combine("pkg-1.0.0", "LICENSE"), "MIT");
        File.WriteAllText(dir.Combine("pkg-1.0.0", "EXTRA.txt"), "x");
        Assert.Equal(["licenses/nuget/pkg-1.0.0/EXTRA.txt is not listed in packages.tsv"], FolderProblems(dir.Path, "pkg-1.0.0", ["LICENSE"]));
    }

    /// <summary>Same rule as packaging/collect-licenses.sh: every listed file exists and is non-empty, and nothing else is in the folder.</summary>
    private static List<string> FolderProblems(string nugetDir, string folder, IReadOnlyList<string> files)
    {
        var dir = Path.Combine(nugetDir, folder);
        if (!Directory.Exists(dir))
            return [$"folder licenses/nuget/{folder} missing"];
        var problems = files
            .Where(f => !File.Exists(Path.Combine(dir, f)) || new FileInfo(Path.Combine(dir, f)).Length == 0)
            .Select(f => $"licenses/nuget/{folder}/{f} missing or empty")
            .ToList();
        problems.AddRange(Directory.GetFiles(dir)
            .Select(Path.GetFileName)
            .Where(name => !files.Contains(name!, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => $"licenses/nuget/{folder}/{name} is not listed in packages.tsv"));
        return problems;
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

    private sealed record ManifestRow(string Folder, IReadOnlyList<string> Files);

    /// <summary>"Id/Version" keys mapped to their license folder and expected files.</summary>
    private static Dictionary<string, ManifestRow> ReadRows()
    {
        var rows = new Dictionary<string, ManifestRow>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(Root, "licenses", "nuget", "packages.tsv")))
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var cols = line.Split('\t');
            Assert.True(cols.Length >= 6, $"Malformed row (expected id, version, license, folder, files, source): {line}");
            var files = cols[4].Split(',', StringSplitOptions.TrimEntries);
            Assert.True(files.Length > 0 && files.All(f => f.Length > 0), $"Row without files: {line}");
            Assert.True(rows.TryAdd($"{cols[0]}/{cols[1]}", new ManifestRow(cols[3], files)), $"Duplicate row: {line}");
        }
        return rows;
    }

    private static Dictionary<string, string> ReadManifest() =>
        ReadRows().ToDictionary(kv => kv.Key, kv => kv.Value.Folder, StringComparer.Ordinal);

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
