using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class FileReplacementPermissionsTests
{
    private const string BlobA = "set realmlist logon-a.example.org\r\n";
    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));

    // 2541 decimal == 04755 octal (setuid + rwxr-xr-x).
    private const int Mode4755 = 2541;
    private const int Mode0444 = 292;

    [Theory]
    [InlineData(-1)]
    [InlineData(Mode4755)]
    [InlineData(0x200)] // sticky bit
    [InlineData(0x8000 | 0x1A4)] // S_IFREG | 0644
    [InlineData(int.MaxValue)]
    public void FromUnixAndWindows_RejectsModesOutsidePermissionBits(int mode)
    {
        Assert.False(FileReplacementPermissions.IsValidUnixMode(mode));
        Assert.Throws<ArgumentOutOfRangeException>(() => FileReplacementPermissions.FromUnixAndWindows(mode, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Mode0444)]
    [InlineData(0x1FF)]
    public void FromUnixAndWindows_AcceptsPlainPermissionBits(int mode)
    {
        Assert.Equal(mode, FileReplacementPermissions.FromUnixAndWindows(mode, null)!.UnixMode);
    }

    [UnixFact]
    public void AtomicWriter_WithOutOfRangeMode_ThrowsAndLeavesTheFileAndNoTempBehind()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("realmlist.wtf");
        File.WriteAllText(path, "old\r\n");

        var writer = new AtomicFileWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            writer.WriteAllBytes(path, "new\r\n"u8.ToArray(), new FileReplacementPermissions(Mode4755, null)));

        Assert.Equal("old\r\n", File.ReadAllText(path));
        Assert.Equal(["realmlist.wtf"], Directory.GetFiles(temp.Path).Select(Path.GetFileName));
        Assert.Equal(0, (int)(File.GetUnixFileMode(path) & UnixFileMode.SetUser));
    }

    [LinuxFact]
    public void AtomicWriter_OnLinux_AppliesUnixModeToTempBeforeReplace()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("realmlist.wtf");
        File.WriteAllText(path, "old\r\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var writer = new AtomicFileWriter();
        writer.WriteAllBytes(path, "new\r\n"u8.ToArray(), new FileReplacementPermissions(Mode0444, null));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead, File.GetUnixFileMode(path));
    }

    [LinuxFact]
    public void Apply_OnSetuidFile_DoesNotCopySetuidBit()
    {
        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var path = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.WriteAllText(path, "set realmlist old.example.org\r\n");
        File.SetUnixFileMode(path, (UnixFileMode)Mode4755);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), temp.Combine("backups"));
        var result = applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));

        Assert.Equal((UnixFileMode)0x1ED, File.GetUnixFileMode(path)); // 0755
        var manifest = File.ReadAllText(Path.Combine(temp.Combine("backups"), "install-1", result.BackupId!, "manifest.json"));
        Assert.Contains("\"unix_mode\": 493", manifest);
    }

    [LinuxFact]
    public void Undo_WithTamperedOutOfRangeUnixMode_IsRejectedBeforeRestoring()
    {
        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var path = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.WriteAllText(path, "set realmlist old.example.org\r\n");
        File.SetUnixFileMode(path, (UnixFileMode)Mode0444);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var result = applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var applied = File.ReadAllText(path);
        var manifestPath = Path.Combine(backups, "install-1", result.BackupId!, "manifest.json");
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace($"\"unix_mode\": {Mode0444}", $"\"unix_mode\": {Mode4755}"));

        var ex = Assert.Throws<ClientFilesException>(() => applier.Undo("install-1", client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal(applied, File.ReadAllText(path));
        Assert.Equal(0, (int)(File.GetUnixFileMode(path) & UnixFileMode.SetUser));
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
