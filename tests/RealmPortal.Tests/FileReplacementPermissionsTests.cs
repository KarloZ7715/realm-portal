using RealmPortal.Core.IO;

namespace RealmPortal.Tests;

public class FileReplacementPermissionsTests
{
    [Fact]
    public void FromUnixAndWindows_MasksSetuidBits()
    {
        var permissions = FileReplacementPermissions.FromUnixAndWindows(0o104755, true);
        Assert.NotNull(permissions);
        Assert.Equal(0o755, permissions!.UnixMode);
    }

    [Fact]
    public void AtomicWriter_OnLinux_AppliesUnixModeToTempBeforeReplace()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var temp = new TempDirectory();
        var path = temp.Combine("realmlist.wtf");
        File.WriteAllText(path, "old\r\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var writer = new AtomicFileWriter();
        var mode = (int)(UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        writer.WriteAllBytes(path, "new\r\n"u8.ToArray(), new FileReplacementPermissions(mode, null));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead, File.GetUnixFileMode(path));
    }
}
