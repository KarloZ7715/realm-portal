using System.Text;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientFilesPlannerTests
{
    private const string Blob = "set realmlist logon.example.org\r\n";
    private const string BlobWithPatch = "set realmlist logon.example.org\r\nset patchlist patch.example.org\r\n";

    private static readonly ClientVersion V1 = new(new Version(1, 12, 1, 5875));
    private static readonly ClientVersion V2 = new(new Version(2, 4, 3, 8606));
    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));
    private static readonly ClientVersion V4 = new(new Version(4, 3, 4, 15595));
    private static readonly ClientVersion V5 = new(new Version(5, 4, 8, 18414));
    private static readonly ClientVersion V6 = new(new Version(6, 2, 4, 21355));
    private static readonly ClientVersion V7 = new(new Version(7, 3, 5, 26972));

    private readonly ClientFilesPlanner _planner = new();

    private static ClientFilesRequest Request(string root, ClientVersion version, string realmlist = Blob, string realm = "") =>
        new("install-1", "profile-1", root, version, realmlist, realm);

    private static string Text(ClientFileEdit edit) => Encoding.Latin1.GetString(edit.NewBytes);

    private static ClientFileEdit Edit(ClientFilesPlan plan, string path) =>
        Assert.Single(plan.Edits, e => e.RelativePath == path);

    private static string Hex(byte[] b) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(b));

    private static void Write(TempDirectory dir, string relative, byte[] bytes)
    {
        var path = dir.Combine(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static void Write(TempDirectory dir, string relative, string text) => Write(dir, relative, Encoding.Latin1.GetBytes(text));

    private static void Apply(TempDirectory dir, ClientFilesPlan plan)
    {
        foreach (var e in plan.Edits)
        {
            var path = dir.Combine(e.RelativePath.Split('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, e.NewBytes);
        }
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(root, p), p => File.Exists(p) ? Hex(File.ReadAllBytes(p)) : "<dir>");

    // ---- layouts per build ----

    [Theory]
    [MemberData(nameof(OldBuilds))]
    public void OldClients_CreateRootRealmlistOnly_AndNeverCreateConfig(ClientVersion version)
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Combine("Data", "enUS"));

        var plan = _planner.Plan(Request(dir.Path, version, realm: "Some Realm"));

        var edit = Assert.Single(plan.Edits);
        Assert.Equal("realmlist.wtf", edit.RelativePath);
        Assert.Equal(ClientEditKind.Create, edit.Kind);
        Assert.Null(edit.Sha256Before);
        Assert.Equal("set realmlist logon.example.org\r\n", Text(edit));
        Assert.Equal(Hex(edit.NewBytes), edit.Sha256After);
    }

    public static TheoryData<ClientVersion> OldBuilds() => new() { V1, V2 };

    [Fact]
    public void OldClients_WithExistingConfig_TouchOnlyTheRealmNameLine()
    {
        using var dir = new TempDirectory();
        const string config = "SET locale \"enUS\"\r\nSET realmName \"Old\"\r\nSET accountName \"bob\"\r\n";
        Write(dir, "WTF/Config.wtf", config);

        var plan = _planner.Plan(Request(dir.Path, V2, realm: "New"));

        Assert.Equal("SET locale \"enUS\"\r\nSET realmName \"New\"\r\nSET accountName \"bob\"\r\n", Text(Edit(plan, "WTF/Config.wtf")));
        Assert.DoesNotContain("realmList", Text(Edit(plan, "WTF/Config.wtf")));
    }

    [Fact]
    public void OldClients_WithExistingConfigAndNoRealm_RemoveTheRealmNameLine()
    {
        using var dir = new TempDirectory();
        Write(dir, "WTF/Config.wtf", "SET locale \"enUS\"\nSET realmName \"Old\"\n");
        Write(dir, "realmlist.wtf", Blob);

        var plan = _planner.Plan(Request(dir.Path, V1));

        Assert.Equal("SET locale \"enUS\"\n", Text(Edit(plan, "WTF/Config.wtf")));
    }

    [Theory]
    [MemberData(nameof(ThreeAndFour))]
    public void Wotlk_And_Cataclysm_WriteEveryExistingLocaleAndConfig_NotRootRealmlist(ClientVersion version)
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Combine("Data", "enUS"));
        Directory.CreateDirectory(dir.Combine("Data", "esES"));
        Directory.CreateDirectory(dir.Combine("Data", "Cache"));   // not a locale
        Directory.CreateDirectory(dir.Combine("Data", "enus"));    // wrong pattern
        Directory.CreateDirectory(dir.Combine("Data", "eng"));     // wrong pattern

        var plan = _planner.Plan(Request(dir.Path, version, BlobWithPatch, "Realm One"));

        Assert.Equal(["Data/enUS/realmlist.wtf", "Data/esES/realmlist.wtf", "WTF/Config.wtf"],
            plan.Edits.Select(e => e.RelativePath).ToArray());
        Assert.All(plan.Edits, e => Assert.Equal(ClientEditKind.Create, e.Kind));
        Assert.Equal(BlobWithPatch, Text(Edit(plan, "Data/enUS/realmlist.wtf")));
        Assert.Equal("SET realmList \"logon.example.org\"\r\nSET patchlist \"patch.example.org\"\r\nSET realmName \"Realm One\"\r\n",
            Text(Edit(plan, "WTF/Config.wtf")));
        Assert.False(Directory.Exists(dir.Combine("Data", "frFR")));
    }

    public static TheoryData<ClientVersion> ThreeAndFour() => new() { V3, V4 };

    [Fact]
    public void Wotlk_WithoutLocaleFolders_WritesOnlyConfig_AndNeverCreatesLocales()
    {
        using var dir = new TempDirectory();

        var plan = _planner.Plan(Request(dir.Path, V3));

        Assert.Equal(["WTF/Config.wtf"], plan.Edits.Select(e => e.RelativePath).ToArray());
        Assert.Equal("SET realmList \"logon.example.org\"\r\n", Text(plan.Edits[0]));
        Assert.False(Directory.Exists(dir.Combine("Data")));
    }

    [Fact]
    public void Wotlk_ExistingRootRealmlist_IsUpdatedToo_ButNeverCreated()
    {
        using var dir = new TempDirectory();
        Write(dir, "RealmList.WTF", "set realmlist old.example.org\n");

        var plan = _planner.Plan(Request(dir.Path, V3));

        var edit = Edit(plan, "RealmList.WTF");
        Assert.Equal(ClientEditKind.Replace, edit.Kind);
        Assert.Equal("set realmlist logon.example.org\n", Text(edit)); // existing LF style kept

        using var empty = new TempDirectory();
        Assert.DoesNotContain(_planner.Plan(Request(empty.Path, V3)).Edits, e => e.RelativePath.Equals("realmlist.wtf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MoP_WritesOnlyConfig()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Combine("Data", "enUS"));

        var plan = _planner.Plan(Request(dir.Path, V5, BlobWithPatch, "R"));

        var edit = Assert.Single(plan.Edits);
        Assert.Equal("WTF/Config.wtf", edit.RelativePath);
        Assert.Equal("SET realmList \"logon.example.org\"\r\nSET patchlist \"patch.example.org\"\r\nSET realmName \"R\"\r\n", Text(edit));
    }

    [Theory]
    [MemberData(nameof(SixAndSeven))]
    public void WodAndLegion_WritePortal_FromRealmlistHost(ClientVersion version)
    {
        using var dir = new TempDirectory();

        var plan = _planner.Plan(Request(dir.Path, version, realm: "R"));

        Assert.Equal("SET portal \"logon.example.org\"\r\nSET realmName \"R\"\r\n", Text(Assert.Single(plan.Edits)));
    }

    public static TheoryData<ClientVersion> SixAndSeven() => new() { V6, V7 };

    [Fact]
    public void WodAndLegion_ExplicitPortalLine_WinsOverTheHost()
    {
        using var dir = new TempDirectory();

        var plan = _planner.Plan(Request(dir.Path, V7, "set realmlist a.example.org\r\nset portal b.example.org\r\n"));

        Assert.Equal("SET portal \"b.example.org\"\r\n", Text(Assert.Single(plan.Edits)));
    }

    // ---- content rules ----

    [Fact]
    public void Realmlist_KeepsExistingCrLfOrLf_NewFileUsesCrLf()
    {
        using var dir = new TempDirectory();
        Write(dir, "Data/enUS/realmlist.wtf", "set realmlist old\n");
        Write(dir, "Data/esES/realmlist.wtf", "set realmlist old\r\n");
        Directory.CreateDirectory(dir.Combine("Data", "frFR"));

        var plan = _planner.Plan(Request(dir.Path, V3, BlobWithPatch));

        Assert.Equal("set realmlist logon.example.org\nset patchlist patch.example.org\n", Text(Edit(plan, "Data/enUS/realmlist.wtf")));
        Assert.Equal(BlobWithPatch, Text(Edit(plan, "Data/esES/realmlist.wtf")));
        Assert.Equal(BlobWithPatch, Text(Edit(plan, "Data/frFR/realmlist.wtf")));
    }

    [Fact]
    public void Realmlist_PreservesBom()
    {
        using var dir = new TempDirectory();
        Write(dir, "realmlist.wtf", Cat([0xEF, 0xBB, 0xBF], Ascii("set realmlist old\r\n")));

        var plan = _planner.Plan(Request(dir.Path, V1));

        var bytes = Edit(plan, "realmlist.wtf").NewBytes;
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("set realmlist logon.example.org\r\n", Encoding.ASCII.GetString(bytes[3..]));
    }

    [Fact]
    public void Realmlist_AcceptsBlobWithLfOrWithoutFinalTerminator_AndWritesTerminatedLines()
    {
        using var dir = new TempDirectory();

        var plan = _planner.Plan(Request(dir.Path, V1, "set realmlist h.example.org\nset patchlist p.example.org"));

        Assert.Equal("set realmlist h.example.org\r\nset patchlist p.example.org\r\n", Text(Edit(plan, "realmlist.wtf")));
    }

    [Fact]
    public void Realmlist_SameContent_IsOmitted()
    {
        using var dir = new TempDirectory();
        Write(dir, "realmlist.wtf", Blob);

        Assert.True(_planner.Plan(Request(dir.Path, V1)).IsEmpty);
    }

    [Fact]
    public void Config_ReplacesValueKeepingCapitalization_AndLeavesOtherLinesByteForByte()
    {
        using var dir = new TempDirectory();
        byte[] Original(string realmlistLine) => Cat(
            Ascii("SET locale \"enUS\"\r\n"),
            Ascii(realmlistLine),
            Ascii("SET accountName \"caf"), [0xE9], Ascii("\"\n"),   // Latin-1 byte and a bare LF
            Ascii("SET gxResolution \"1920x1080\"   \r\n"),
            Ascii("# comment without terminator"));
        Write(dir, "WTF/Config.wtf", Original("set\t realmlist\t\"old.example.org\"\r\n"));

        var plan = _planner.Plan(Request(dir.Path, V3));

        // Only the value of the realmList line changes; the unterminated last line stays as it was.
        Assert.Equal(Original("set\t realmlist\t\"logon.example.org\"\r\n"), Edit(plan, "WTF/Config.wtf").NewBytes);
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void Config_AppendsMissingManagedLines_UsingTheFilesLineEnding()
    {
        using var dir = new TempDirectory();
        Write(dir, "WTF/Config.wtf", "SET locale \"enUS\"\nSET accountName \"bob\"");

        var plan = _planner.Plan(Request(dir.Path, V3, BlobWithPatch, "R"));

        Assert.Equal("SET locale \"enUS\"\nSET accountName \"bob\"\nSET realmList \"logon.example.org\"\nSET patchlist \"patch.example.org\"\nSET realmName \"R\"\n",
            Text(Edit(plan, "WTF/Config.wtf")));
    }

    [Fact]
    public void Config_DuplicateManagedKeys_FirstReplaced_RepeatsDeleted()
    {
        using var dir = new TempDirectory();
        Write(dir, "WTF/Config.wtf",
            "SET realmList \"a\"\r\nSET locale \"enUS\"\r\nSET REALMLIST \"b\"\r\nSET realmName \"x\"\r\nset realmname \"y\"\r\nSET accountName \"bob\"\r\nSET patchlist \"p1\"\r\nSET patchlist \"p2\"\r\n");

        var plan = _planner.Plan(Request(dir.Path, V3, BlobWithPatch, "Real"));

        Assert.Equal(
            "SET realmList \"logon.example.org\"\r\nSET locale \"enUS\"\r\nSET realmName \"Real\"\r\nSET accountName \"bob\"\r\nSET patchlist \"patch.example.org\"\r\n",
            Text(Edit(plan, "WTF/Config.wtf")));
    }

    [Fact]
    public void Config_RealmNameAbsent_RemovesAllRealmNameLines_AndKeepsTheRest()
    {
        using var dir = new TempDirectory();
        Write(dir, "WTF/Config.wtf", "SET realmList \"logon.example.org\"\r\nSET realmName \"Other\"\r\nSET locale \"enUS\"\r\nSET realmName \"Dup\"\r\n");

        var plan = _planner.Plan(Request(dir.Path, V3, realm: ""));

        Assert.Equal("SET realmList \"logon.example.org\"\r\nSET locale \"enUS\"\r\n", Text(Edit(plan, "WTF/Config.wtf")));
    }

    [Fact]
    public void Config_PatchlistAbsentFromText_LeavesExistingPatchlistLineAlone()
    {
        using var dir = new TempDirectory();
        Write(dir, "WTF/Config.wtf", "SET patchlist \"keep\"\r\n");

        var plan = _planner.Plan(Request(dir.Path, V3));

        Assert.Equal("SET patchlist \"keep\"\r\nSET realmList \"logon.example.org\"\r\n", Text(Edit(plan, "WTF/Config.wtf")));
    }

    [Fact]
    public void Config_NewFile_HoldsOnlyManagedKeys_NeverLocale()
    {
        using var dir = new TempDirectory();

        var text = Text(Assert.Single(_planner.Plan(Request(dir.Path, V5, realm: "R")).Edits));

        Assert.DoesNotContain("locale", text);
        Assert.DoesNotContain("accountName", text);
    }

    [Fact]
    public void Config_PreservesBom_AndNonAsciiRealmIsWrittenAsUtf8()
    {
        using var dir = new TempDirectory();
        Write(dir, "WTF/Config.wtf", Cat([0xEF, 0xBB, 0xBF], Ascii("SET locale \"esES\"\r\n")));

        var plan = _planner.Plan(Request(dir.Path, V5, realm: "Reino Épico"));
        var bytes = Edit(plan, "WTF/Config.wtf").NewBytes;

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("SET locale \"esES\"\r\nSET realmList \"logon.example.org\"\r\nSET realmName \"Reino Épico\"\r\n",
            Encoding.UTF8.GetString(bytes[3..]));
    }

    // ---- case-insensitive paths ----

    [Fact]
    public void Paths_AreResolvedCaseInsensitively_AndKeepTheRealNames()
    {
        using var dir = new TempDirectory();
        Write(dir, "data/enUS/REALMLIST.wtf", "set realmlist old\r\n");
        Write(dir, "wtf/config.WTF", "SET realmList \"old\"\r\n");

        var plan = _planner.Plan(Request(dir.Path, V3));

        Assert.Equal(["data/enUS/REALMLIST.wtf", "wtf/config.WTF"], plan.Edits.Select(e => e.RelativePath).ToArray());
        Assert.All(plan.Edits, e => Assert.Equal(ClientEditKind.Replace, e.Kind));
    }

    // ---- hashes, dry run and idempotence ----

    [Fact]
    public void Plan_HashesMatchTheContents_AndNothingIsWritten()
    {
        using var dir = new TempDirectory();
        var oldBytes = "set realmlist old\r\n"u8.ToArray();
        Write(dir, "Data/enUS/realmlist.wtf", oldBytes);
        var before = Snapshot(dir.Path);

        var plan = _planner.Plan(Request(dir.Path, V3));

        Assert.Equal(before, Snapshot(dir.Path));
        var edit = Edit(plan, "Data/enUS/realmlist.wtf");
        Assert.Equal(Hex(oldBytes), edit.Sha256Before);
        Assert.Equal(Hex(edit.NewBytes), edit.Sha256After);
    }

    [Fact]
    public void Plan_AfterApplyingIt_IsEmpty()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Combine("Data", "enUS"));
        var request = Request(dir.Path, V3, BlobWithPatch, "R");

        Apply(dir, _planner.Plan(request));

        Assert.True(_planner.Plan(request).IsEmpty);
    }

    // ---- validation ----

    [Theory]
    [InlineData("")]
    [InlineData("set realmlist")]
    [InlineData("set realmlist \r\n")]
    [InlineData("set realmlist host\r\nset foo bar\r\n")]               // unknown key
    [InlineData("set realmlist host\r\nset portal p\r\n")]              // portal only for WoD/Legion
    [InlineData("set realmlist \"host\"\r\n")]                          // quotes
    [InlineData("set realmlist ho st\r\n")]                             // space in value
    [InlineData("set realmlist h\u00F3st\r\n")]                         // non-ASCII
    [InlineData("SET realmlist host\r\n")]                              // uppercase SET
    [InlineData("set REALMLIST host\r\n")]                              // uppercase key
    [InlineData("set patchlist p\r\nset realmlist host\r\n")]           // first line must be realmlist
    [InlineData("set realmlist host\r\n\r\nset patchlist p\r\n")]       // blank line
    [InlineData("set realmlist host\r\nset realmlist other\r\n")]       // duplicate key
    [InlineData("set realmlist host\rset patchlist p\r\n")]             // bare CR
    [InlineData("set realmlist host\r\nhello\r\n")]                     // not a set line
    [InlineData("set realmlist host\t\r\n")]                            // control character
    public void InvalidRealmlist_IsRejected_AndNothingIsPlanned(string blob)
    {
        using var dir = new TempDirectory();

        var ex = Assert.Throws<ClientFilesException>(() => _planner.Plan(Request(dir.Path, V3, blob)));

        Assert.Equal(ClientFilesErrorCode.InvalidRealmlist, ex.Code);
    }

    [Fact]
    public void InvalidRealm_QuotesOrControlCharacters_AreRejected()
    {
        using var dir = new TempDirectory();

        Assert.Equal(ClientFilesErrorCode.InvalidRealmlist,
            Assert.Throws<ClientFilesException>(() => _planner.Plan(Request(dir.Path, V3, realm: "a\"b"))).Code);
        Assert.Equal(ClientFilesErrorCode.InvalidRealmlist,
            Assert.Throws<ClientFilesException>(() => _planner.Plan(Request(dir.Path, V3, realm: "a\nb"))).Code);
    }

    [Fact]
    public void Portal_IsAcceptedOnlyForWodAndLegion()
    {
        using var dir = new TempDirectory();
        const string blob = "set realmlist h.example.org\r\nset portal p.example.org\r\n";

        Assert.Equal(ClientFilesErrorCode.InvalidRealmlist,
            Assert.Throws<ClientFilesException>(() => _planner.Plan(Request(dir.Path, V5, blob))).Code);
        _planner.Plan(Request(dir.Path, V6, blob));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(11)]
    public void UnsupportedClientVersion_IsRejected(int major)
    {
        using var dir = new TempDirectory();

        var ex = Assert.Throws<ClientFilesException>(() => _planner.Plan(Request(dir.Path, new ClientVersion(new Version(major, 0, 0, 1)))));

        Assert.Equal(ClientFilesErrorCode.UnsupportedClient, ex.Code);
    }

    [Fact]
    public void MissingClientDirectory_IsPathNotFound()
    {
        using var dir = new TempDirectory();

        var ex = Assert.Throws<ClientFilesException>(() => _planner.Plan(Request(dir.Combine("nope"), V3)));

        Assert.Equal(ClientFilesErrorCode.PathNotFound, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyClientDirectory_IsPathNotFound(string root)
    {
        var ex = Assert.Throws<ClientFilesException>(() => _planner.Plan(Request(root, V3)));

        Assert.Equal(ClientFilesErrorCode.PathNotFound, ex.Code);
    }

    [Fact]
    public void UnsafeIds_AreRejectedWithArgumentException()
    {
        using var dir = new TempDirectory();

        Assert.Throws<ArgumentException>(() => _planner.Plan(new ClientFilesRequest("../x", "p", dir.Path, V3, Blob, "")));
    }
}
