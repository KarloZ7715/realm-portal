using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class FileReplacementPermissionsTests
{
    [Fact]
    public void FromUnixAndWindows_MasksSetuidBits()
    {
        var withSetUid = (int)(UnixFileMode.SetUser | UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var permissions = FileReplacementPermissions.FromUnixAndWindows(withSetUid, true);
        Assert.NotNull(permissions);
        Assert.Equal(withSetUid & 0x1FF, permissions!.UnixMode);
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
