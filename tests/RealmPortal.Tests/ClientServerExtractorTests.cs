using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientServerExtractorTests
{
    private const string ExampleHost = "logon.example.org";

    private readonly ClientServerExtractor _extractor = new();

    [Fact]
    public void Extract_ReadsLocaleRealmlistAndConfig()
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\n");
        WriteConfig(dir, "SET realmName \"Icecrown\"\r\nSET accountName \"player1\"\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));

        Assert.Equal(ExampleHost, result.Host);
        Assert.Equal("Icecrown", result.Realm);
        Assert.Equal("player1", result.Account);
        Assert.Null(result.PatchlistHost);
        Assert.NotNull(result.RealmlistPath);
        Assert.NotNull(result.ConfigPath);
    }

    [Fact]
    public void Extract_PrefersLocaleRealmlistForWotlkBuild()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.Combine("realmlist.wtf"), "set realmlist root.example.org\r\n");
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));

        Assert.Equal(ExampleHost, result.Host);
    }

    [Fact]
    public void Extract_PrefersRootRealmlistForVanillaBuild()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.Combine("realmlist.wtf"), "set realmlist root.example.org\r\n");
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 5875));

        Assert.Equal("root.example.org", result.Host);
    }

    [Fact]
    public void Extract_ReadsRootRealmlistForAnyBuild()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.Combine("realmlist.wtf"), $"set realmlist {ExampleHost}\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));

        Assert.Equal(ExampleHost, result.Host);
    }

    [Fact]
    public void Extract_FallsBackToConfigRealmList()
    {
        using var dir = new TempDirectory();
        WriteConfig(dir, $"SET locale \"enUS\"\r\nSET realmList \"{ExampleHost}\"\r\nSET realmName \"Icecrown\"\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));

        Assert.Equal(ExampleHost, result.Host);
        Assert.Equal("Icecrown", result.Realm);
        Assert.Null(result.RealmlistPath);
    }

    [Fact]
    public void Extract_ReadsPatchlistFromRealmlist()
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\nset patchlist patch.example.org\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));

        Assert.Equal("patch.example.org", result.PatchlistHost);
        Assert.NotNull(result.ExtraRealmlistLines);
        Assert.Equal("set patchlist patch.example.org", Assert.Single(result.ExtraRealmlistLines!));
    }

    [Fact]
    public void Extract_ReadsPatchlistFromConfig()
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\n");
        WriteConfig(dir, "SET patchlist \"http://patch.example.org/files\"\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));

        Assert.Equal("http://patch.example.org/files", result.PatchlistHost);
    }

    [Fact]
    public void Extract_IgnoresAnInvalidPatchlistButKeepsTheHost()
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\nset patchlist http://\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));

        Assert.Equal(ExampleHost, result.Host);
        Assert.Null(result.PatchlistHost);
    }

    [Theory]
    [InlineData("logon.battle.net")]
    [InlineData("us.logon.worldofwarcraft.com")]
    [InlineData("BATTLE.NET")]
    public void Extract_SkipsOfficialPlaceholderHosts(string host)
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {host}\r\n");

        Assert.Null(_extractor.Extract(dir.Path, 12340));
    }

    [Fact]
    public void Extract_UsesConfigHostWhenTheRealmlistIsAPlaceholder()
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", "set realmlist logon.battle.net\r\n");
        WriteConfig(dir, $"SET realmList \"{ExampleHost}\"\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));

        Assert.Equal(ExampleHost, result.Host);
    }

    [Fact]
    public void Extract_WithoutAnyHostReturnsNull()
    {
        using var dir = new TempDirectory();
        WriteConfig(dir, "SET locale \"enUS\"\r\nSET realmName \"Icecrown\"\r\n");

        Assert.Null(_extractor.Extract(dir.Path, 12340));
    }

    [Fact]
    public void Extract_WithAnInvalidHostReturnsNull()
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", "set realmlist bad host\r\n");

        Assert.Null(_extractor.Extract(dir.Path, 12340));
    }

    [Fact]
    public void Extract_MissingFolderReturnsNull()
    {
        using var dir = new TempDirectory();

        Assert.Null(_extractor.Extract(dir.Combine("does-not-exist"), 12340));
    }

    [Fact]
    public void Extract_IgnoresAnOversizedRealmlist()
    {
        using var dir = new TempDirectory();
        var padding = new string('a', ClientServerExtractor.MaxRealmlistBytes);
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\nset patchlist {padding}.example.org\r\n");

        Assert.Null(_extractor.Extract(dir.Path, 12340));
    }

    [Fact]
    public void Extract_IgnoresAnOversizedConfig()
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\n");
        var padding = new string('a', ClientServerExtractor.MaxConfigBytes);
        WriteConfig(dir, $"SET realmName \"{padding}\"\r\nSET accountName \"player1\"\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));

        Assert.Equal(ExampleHost, result.Host);
        Assert.Null(result.Account);
    }

    [Fact]
    public void Extract_DoesNotTreatARegionPortalAsAHost()
    {
        using var dir = new TempDirectory();
        WriteConfig(dir, "SET portal \"us\"\r\nSET realmName \"Icecrown\"\r\n");

        Assert.Null(_extractor.Extract(dir.Path, 12340));
    }

    [Fact]
    public void Extract_AcceptsAnAddressPortal()
    {
        using var dir = new TempDirectory();
        WriteConfig(dir, $"SET portal \"{ExampleHost}\"\r\n");

        var result = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));

        Assert.Equal(ExampleHost, result.Host);
    }

    [Fact]
    public void Extract_DoesNotModifyTheClientFolder()
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\n");
        WriteConfig(dir, "SET realmName \"Icecrown\"\r\nSET accountName \"player1\"\r\n");
        var before = Snapshot(dir.Path);

        _ = _extractor.Extract(dir.Path, 12340);

        Assert.Equal(before, Snapshot(dir.Path));
    }

    [Fact]
    public void Extract_RefusesAConfigReachedThroughASymlinkedFolder()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var client = new TempDirectory();
        using var outside = new TempDirectory();
        File.WriteAllText(outside.Combine("Config.wtf"), $"SET realmList \"{ExampleHost}\"\r\n");
        Directory.CreateSymbolicLink(client.Combine("WTF"), outside.Path);

        Assert.Null(_extractor.Extract(client.Path, 12340));
    }

    [Fact]
    public void Extract_RefusesASymlinkedConfigFile()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var client = new TempDirectory();
        using var outside = new TempDirectory();
        File.WriteAllText(outside.Combine("Config.wtf"), $"SET realmList \"{ExampleHost}\"\r\n");
        Directory.CreateDirectory(client.Combine("WTF"));
        File.CreateSymbolicLink(client.Combine("WTF", "Config.wtf"), outside.Combine("Config.wtf"));

        Assert.Null(_extractor.Extract(client.Path, 12340));
    }

    /// <summary>The extracted values build the server the registration layer is expected to add.</summary>
    [Fact]
    public void Extract_ProducesValuesServerCreateAccepts()
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\n");
        WriteConfig(dir, "SET realmName \"Icecrown\"\r\nSET accountName \"player1\"\r\n");

        var extracted = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));
        var server = Server.Create("My Server", extracted.Host, extracted.Realm, extracted.ExtraRealmlistLines, 12340, extracted.Account, clientId: "c0123456789abcdef");

        Assert.Equal(ExampleHost, server.Host);
        Assert.Equal("Icecrown", server.Realm);
        Assert.Equal("player1", server.Account);
        Assert.Equal(12340, server.RequiredClientBuild);
    }

    [Fact]
    public void Identity_MatchesAnEquivalentStoredServer()
    {
        using var dir = new TempDirectory();
        WriteLocaleRealmlist(dir, "enUS", $"set realmlist {ExampleHost}\r\n");
        WriteConfig(dir, "SET realmName \"Icecrown\"\r\n");

        var extracted = Assert.IsType<ExtractedServer>(_extractor.Extract(dir.Path, 12340));
        var stored = Server.Create("My Server", ExampleHost, "Icecrown", requiredClientBuild: 12340);

        Assert.Equal(RealmlistIdentity.FromServer(stored), extracted.Identity(12340));
        Assert.Equal(RealmlistIdentity.FromServer(stored).GetHashCode(), extracted.Identity(12340).GetHashCode());
    }

    [Fact]
    public void Identity_IgnoresHostAndRealmCase()
    {
        var lower = RealmlistIdentity.Create("logon.example.org", "icecrown", 12340);
        var upper = RealmlistIdentity.Create("LOGON.EXAMPLE.ORG", "Icecrown", 12340);

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
    }

    [Fact]
    public void Identity_DiffersByBuildAndRealm()
    {
        var baseline = RealmlistIdentity.Create("logon.example.org", "Icecrown", 12340);

        Assert.NotEqual(baseline, RealmlistIdentity.Create("logon.example.org", "Icecrown", 5875));
        Assert.NotEqual(baseline, RealmlistIdentity.Create("logon.example.org", "Deathwing", 12340));
        Assert.NotEqual(baseline, RealmlistIdentity.Create("other.example.org", "Icecrown", 12340));
        Assert.NotEqual(baseline, RealmlistIdentity.Create("logon.example.org", "Icecrown", null));
    }

    [Fact]
    public void Identity_TreatsAMissingRealmAsEmpty()
    {
        Assert.Equal(RealmlistIdentity.Create("logon.example.org", null, 12340),
            RealmlistIdentity.Create("logon.example.org", "", 12340));
    }

    private static void WriteLocaleRealmlist(TempDirectory dir, string locale, string content)
    {
        var folder = dir.Combine("Data", locale);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "realmlist.wtf"), content);
    }

    private static void WriteConfig(TempDirectory dir, string content)
    {
        var folder = dir.Combine("WTF");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Config.wtf"), content);
    }

    private static SortedDictionary<string, string> Snapshot(string root)
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            entries[Path.GetRelativePath(root, file)] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        return entries;
    }
}
