using System.Globalization;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>#98: an imported server is named after its realm (or its host when there is none), never "realm (host)".</summary>
public sealed class ImportedServerNameTests
{
    private const string Host = "realm.example.test";

    private static ExtractedServer Extracted(string? realm, string host = Host) => new(host, realm, null, null, null, null);

    private static IReadOnlyList<Server> Existing(params string[] names) =>
        names.Select((name, i) => Server.Create(name, $"h{i}.example.test")).ToArray();

    [Theory]
    [InlineData("Example Realm", "Example Realm")]
    [InlineData("  Example Realm  ", "Example Realm")]
    [InlineData("Exa\u0007mple\tRealm", "ExampleRealm")]
    public void Realm_IsTheName_WithoutHost(string realm, string expected) =>
        Assert.Equal(expected, PortalService.CreateImportedServerName(Extracted(realm), []));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0001\u0002")]
    public void NoRealm_UsesHostOnly(string? realm) =>
        Assert.Equal(Host, PortalService.CreateImportedServerName(Extracted(realm), []));

    [Fact]
    public void HostWithPort_IsKeptAsIs() =>
        Assert.Equal("realm.example.test:3725", PortalService.CreateImportedServerName(Extracted(null, "realm.example.test:3725"), []));

    [Fact]
    public void Collisions_GetNumberedSuffix_CaseInsensitively()
    {
        Assert.Equal("Example Realm #2", PortalService.CreateImportedServerName(Extracted("Example Realm"), Existing("example realm")));
        Assert.Equal("Example Realm #3",
            PortalService.CreateImportedServerName(Extracted("Example Realm"), Existing("Example Realm", "EXAMPLE REALM #2")));
        Assert.Equal($"{Host} #2", PortalService.CreateImportedServerName(Extracted(null), Existing(Host)));
    }

    [Fact]
    public void LongRealm_IsCappedAt64_IncludingSuffix()
    {
        var realm = new string('r', 80);
        var first = PortalService.CreateImportedServerName(Extracted(realm), []);
        Assert.Equal(new string('r', Server.MaxNameLength), first);

        var second = PortalService.CreateImportedServerName(Extracted(realm), Existing(first));
        Assert.Equal(new string('r', Server.MaxNameLength - 3) + " #2", second);
        Assert.Equal(Server.MaxNameLength, second.Length);
    }

    private const string Emoji = "\U0001F409";          // dragon, one surrogate pair
    private const string Combining = "e\u0301";        // e + combining acute
    private const string ZwjFamily = "\U0001F468\u200D\U0001F469\u200D\U0001F467"; // 8 chars, one text element
    private const string Flag = "\U0001F1EA\U0001F1F8"; // regional indicators, one text element

    [Theory]
    [InlineData(Emoji)]
    [InlineData(Combining)]
    [InlineData(Flag)]
    public void LongRealm_IsCutAtTextElementBoundary_NeverSplittingIt(string element)
    {
        // 63 'r' then a 2- or 4-char element: char-based cutting at 64 would split it.
        var realm = new string('r', Server.MaxNameLength - 1) + element + "tail";

        var first = PortalService.CreateImportedServerName(Extracted(realm), []);
        Assert.Equal(new string('r', Server.MaxNameLength - 1), first);

        var withElementInside = new string('r', Server.MaxNameLength - 4) + element + "tail";
        var second = PortalService.CreateImportedServerName(Extracted(withElementInside), Existing(PortalService.CreateImportedServerName(Extracted(withElementInside), [])));
        Assert.EndsWith(" #2", second);
        Assert.True(second.Length <= Server.MaxNameLength);
        AssertWholeTextElements(second);
    }

    [Fact]
    public void Suffix_DropsWholeEmojiInsteadOfHalfOfIt()
    {
        var realm = new string('r', Server.MaxNameLength - 4) + Emoji + Emoji; // exactly 64 chars
        var first = PortalService.CreateImportedServerName(Extracted(realm), []);
        Assert.Equal(realm, first);

        // " #2" leaves room for 61 chars: 60 'r' + the 2-char emoji would be 62, so the emoji goes whole.
        var second = PortalService.CreateImportedServerName(Extracted(realm), Existing(first));

        Assert.Equal(new string('r', Server.MaxNameLength - 4) + " #2", second);
    }

    [Theory]
    [InlineData("abc", 2, "ab")]
    [InlineData("a" + Emoji, 2, "a")]
    [InlineData("a" + Emoji, 3, "a" + Emoji)]
    [InlineData(Combining + "x", 1, "")]
    [InlineData("a" + ZwjFamily, 8, "a")]
    [InlineData("a" + ZwjFamily, 9, "a" + ZwjFamily)]
    [InlineData("short", 64, "short")]
    public void TruncateToTextElements_KeepsWholeElements(string value, int max, string expected) =>
        Assert.Equal(expected, PortalService.TruncateToTextElements(value, max));

    [Property(MaxTest = 300)]
    public Property ImportedName_NeverHasLoneSurrogates_AndFitsTheCap(NonNegativeInt fill, NonNegativeInt pick, bool collide)
    {
        string[] pieces = ["r", Emoji, Combining, Flag, "\u00F1", "\U0001F44D\U0001F3FD"];
        var builder = new StringBuilder();
        for (var i = 0; i < 40 + fill.Get % 40; i++)
            builder.Append(pieces[(pick.Get + i * 7) % pieces.Length]);
        var realm = builder.ToString();
        var first = PortalService.CreateImportedServerName(Extracted(realm), []);
        var name = collide ? PortalService.CreateImportedServerName(Extracted(realm), Existing(first)) : first;

        return (name.Length <= Server.MaxNameLength
                && !HasLoneSurrogate(name)
                && realm.StartsWith(collide ? name[..^3] : name, StringComparison.Ordinal)
                && (!collide || name.EndsWith(" #2", StringComparison.Ordinal)))
            .Label($"realm={realm} name={name}");
    }

    private static bool HasLoneSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                i++;
            else if (char.IsSurrogate(value[i]))
                return true;
        }
        return false;
    }

    private static void AssertWholeTextElements(string value)
    {
        Assert.False(HasLoneSurrogate(value));
        Assert.False(value.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(value[0]) == UnicodeCategory.NonSpacingMark);
    }

    [Fact]
    public void Scan_NamesImportedServerAfterRealm_AndKeepsUserNamedServers()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("candidates");
        var client = Path.Combine(root, "client");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        Directory.CreateDirectory(temp.Combine("backups"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        File.WriteAllText(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), $"set realmlist {Host}\r\n", Encoding.Latin1);
        File.WriteAllText(Path.Combine(client, "WTF", "Config.wtf"), "SET realmName \"Example Realm\"\n");
        var service = new PortalService(temp.Combine("config"), temp.Combine("projection"), new PeClientVersionReader(), new NoLaunch(),
            new NeverRunning(), _ => temp.Combine("backups"),
            scanOptions: new ClientScanOptions { IncludeDefaultRoots = false, IncludeSystemRoots = false });
        var mine = service.AddServer(new NewServer("Example Realm", "other.example.test")).Value!;

        var scan = service.Scan(new ScanRequest(AdditionalRoots: [root]));

        Assert.True(scan.Succeeded, scan.Error?.ToString());
        Assert.Equal("Example Realm #2", Assert.Single(scan.Value!.ImportedServers).Name);
        Assert.Equal("Example Realm", service.ListServers().Value!.Single(s => s.Id == mine.Id).Name);
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("Not expected.");
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
