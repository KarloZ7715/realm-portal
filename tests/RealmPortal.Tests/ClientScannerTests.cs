using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientScannerTests
{
    private static readonly Version Wotlk = new(3, 3, 5, 12340);

    private readonly ClientScanner _scanner = new();

    [Fact]
    public void Scan_FindsClientInAdditionalRoot()
    {
        using var home = new TempDirectory();
        var client = CreateClient(home.Combine("client"));

        var found = Scan(home, additional: [home.Combine("client")]);

        var result = Assert.Single(found);
        Assert.Equal(ClientPaths.CanonicalRoot(client), result.Folder);
        Assert.Equal(12340, result.Build);
        Assert.Null(result.VersionError);
        Assert.Equal(ClientOrigin.Discovered, result.Origin);
    }

    [Fact]
    public void Scan_IdMatchesTheStoredClientId()
    {
        using var home = new TempDirectory();
        var client = CreateClient(home.Combine("client"));

        var result = Assert.Single(Scan(home, additional: [home.Path]));

        Assert.Equal(ClientIds.FromFolderPath(client), result.Id);
    }

    [Fact]
    public void Scan_TagsStandardGamesFolderAsDiscovered()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine("Games", "WoW"));

        var result = Assert.Single(Scan(home));

        Assert.Equal(ClientOrigin.Discovered, result.Origin);
        Assert.Equal("WoW", Path.GetFileName(result.Folder));
    }

    [Fact]
    public void Scan_TagsWinePrefixAsWine()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine(".wine", "drive_c", "Program Files", "World of Warcraft"));

        var result = Assert.Single(Scan(home));

        Assert.Equal(ClientOrigin.Wine, result.Origin);
    }

    [Fact]
    public void Scan_TagsLutrisRunnerWinePrefixAsWine()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine(".local", "share", "wineprefixes", "wow", "drive_c", "Games", "WoW"));

        var result = Assert.Single(Scan(home));

        Assert.Equal(ClientOrigin.Wine, result.Origin);
    }

    [Fact]
    public void Scan_TagsBottlePrefixAsWinePrefix()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine(".local", "share", "bottles", "bottles", "mybottle", "drive_c", "Games", "WoW"));

        var result = Assert.Single(Scan(home));

        Assert.Equal(ClientOrigin.WinePrefix, result.Origin);
        Assert.Equal(ClientOrigin.Wine, result.Origin);
    }

    [Fact]
    public void Scan_TagsLutrisGameConfigClientAsLutris()
    {
        using var home = new TempDirectory();
        var client = CreateClient(home.Combine("Games", "MyWoW"));
        var configDirectory = home.Combine(".config", "lutris", "games");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllLines(home.Combine(".config", "lutris", "games", "wow.yml"),
        [
            "game:",
            "  exe: " + Path.Combine(client, "Wow.exe"),
            "  prefix: " + client,
        ]);

        var result = Assert.Single(Scan(home));

        Assert.Equal(ClientOrigin.Lutris, result.Origin);
    }

    [Fact]
    public void Scan_PrefersLutrisOverDiscoveredForTheSameFolder()
    {
        using var home = new TempDirectory();
        var client = CreateClient(home.Combine("Games", "MyWoW"));
        var configDirectory = home.Combine(".local", "share", "lutris", "games");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(home.Combine(".local", "share", "lutris", "games", "wow.yml"), $"exe: {Path.Combine(client, "Wow.exe")}\n");

        var result = Assert.Single(Scan(home));

        Assert.Equal(ClientOrigin.Lutris, result.Origin);
    }

    [Fact]
    public void Scan_TagsSteamCommonClientAsSteam()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine(".steam", "steam", "steamapps", "common", "World of Warcraft"));

        var result = Assert.Single(Scan(home));

        Assert.Equal(ClientOrigin.Steam, result.Origin);
    }

    [Fact]
    public void Scan_FollowsSteamLibraryFoldersVdf()
    {
        using var home = new TempDirectory();
        var library = home.Combine("second-library");
        CreateClient(Path.Combine(library, "steamapps", "common", "WoW"));
        var steamapps = home.Combine(".steam", "steam", "steamapps");
        Directory.CreateDirectory(steamapps);
        File.WriteAllText(Path.Combine(steamapps, "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path"      "{{library.Replace("\\", "\\\\")}}"
                }
            }
            """);

        var result = Assert.Single(Scan(home));

        Assert.Equal(ClientOrigin.Steam, result.Origin);
        Assert.Equal("WoW", Path.GetFileName(result.Folder));
    }

    [Fact]
    public void Scan_FindsSteamCompatDataClient()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine(".steam", "steam", "steamapps", "compatdata", "1234", "pfx", "drive_c", "Program Files", "WoW"));

        var result = Assert.Single(Scan(home));

        Assert.Equal(ClientOrigin.Steam, result.Origin);
    }

    [Fact]
    public void Scan_ReportsUnreadableVersionInsteadOfFailing()
    {
        using var home = new TempDirectory();
        var client = home.Combine("client");
        Directory.CreateDirectory(client);
        File.WriteAllText(Path.Combine(client, "Wow.exe"), "not a portable executable");

        var result = Assert.Single(Scan(home, additional: [client]));

        Assert.Null(result.Version);
        Assert.Equal(ClientVersionErrorKind.NotAPortableExecutable, result.VersionError);
    }

    [Fact]
    public void Scan_IsIdempotent()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine("Games", "One"));
        CreateClient(home.Combine("Games", "Two"));

        var first = Scan(home);
        var second = Scan(home);

        Assert.Equal(first.Select(c => c.Id), second.Select(c => c.Id));
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public void Scan_SkipsAlreadyKnownIds()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine("Games", "One"));
        var known = CreateClient(home.Combine("Games", "Two"));
        var knownId = ClientIds.FromFolderPath(known);

        var found = Scan(home, knownIds: new HashSet<string> { knownId });

        var result = Assert.Single(found);
        Assert.Equal("One", Path.GetFileName(result.Folder));
    }

    [Fact]
    public void Scan_StopsAtTheDepthBudget()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine("a", "b", "c", "Wow"));

        var shallow = Scan(home, additional: [home.Path], maxDepth: 3);
        var deep = Scan(home, additional: [home.Path], maxDepth: 4);

        Assert.Empty(shallow);
        Assert.Single(deep);
    }

    [Fact]
    public void Scan_StopsAtTheDirectoryBudget()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine("Games", "One"));
        CreateClient(home.Combine("Games", "Two"));

        var found = Scan(home, maxDirectories: 1);

        Assert.Empty(found);
    }

    [Fact]
    public void Scan_ReportsProgress()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine("Games", "One"));
        var progress = new Collector<ClientScanProgress>();

        _ = Scan(home, progress: progress);

        Assert.NotEmpty(progress.Items);
        Assert.Equal(progress.Items.Max(item => item.DirectoriesInspected), progress.Items[^1].DirectoriesInspected);
        Assert.Equal(1, progress.Items[^1].ClientsFound);
        Assert.All(progress.Items, item => Assert.False(string.IsNullOrEmpty(item.CurrentSource)));
    }

    [Fact]
    public void Scan_DoesNotModifyCandidateFolders()
    {
        using var home = new TempDirectory();
        var client = CreateClient(home.Combine("Games", "One"));
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        File.WriteAllText(Path.Combine(client, "WTF", "Config.wtf"), "SET realmName \"Icecrown\"\r\n");
        var before = Snapshot(home.Path);

        _ = Scan(home);

        Assert.Equal(before, Snapshot(home.Path));
    }

    [Fact]
    public void Scan_DoesNotFollowSymlinkedFolders()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var home = new TempDirectory();
        using var outside = new TempDirectory();
        CreateClient(outside.Combine("WoW"));
        Directory.CreateDirectory(home.Combine("Games"));
        Directory.CreateSymbolicLink(home.Combine("Games", "linked"), outside.Combine("WoW"));

        var found = Scan(home);

        Assert.Empty(found);
    }

    [Fact]
    public void Scan_WithNoDefaultRootsAndNoAdditionalRootsFindsNothing()
    {
        using var home = new TempDirectory();
        CreateClient(home.Combine("Games", "One"));

        var found = _scanner.Scan(new ClientScanOptions { IncludeDefaultRoots = false, HomeDirectory = home.Path });

        Assert.Empty(found);
    }

    private IReadOnlyList<DiscoveredClient> Scan(
        TempDirectory home,
        IReadOnlyList<string>? additional = null,
        IReadOnlySet<string>? knownIds = null,
        int maxDepth = ClientScanner.DefaultMaxDepth,
        int maxDirectories = ClientScanner.DefaultMaxDirectoriesVisited,
        IProgress<ClientScanProgress>? progress = null) =>
        _scanner.Scan(
            new ClientScanOptions
            {
                HomeDirectory = home.Path,
                IncludeSystemRoots = false,
                AdditionalRoots = additional ?? [],
                KnownClientIds = knownIds,
                MaxDepth = maxDepth,
                MaxDirectoriesVisited = maxDirectories,
            },
            progress);

    private static string CreateClient(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Wow.exe"), SyntheticPe.WithVersion(Wotlk));
        return folder;
    }

    private static SortedDictionary<string, string> Snapshot(string root)
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            entries[Path.GetRelativePath(root, file)] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        return entries;
    }

    private sealed class Collector<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];

        public void Report(T value) => Items.Add(value);
    }
}
