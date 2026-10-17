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
    public void TryNormalizeUnixMode_RejectsNegativeAndHugeValues()
    {
        Assert.False(FileReplacementPermissions.TryNormalizeUnixMode(-1, out _));
        Assert.False(FileReplacementPermissions.TryNormalizeUnixMode(int.MaxValue, out _));
        Assert.Null(FileReplacementPermissions.FromUnixAndWindows(-1, null));
        Assert.Null(FileReplacementPermissions.FromUnixAndWindows(int.MaxValue, null));
    }

    [Fact]
    public void TryNormalizeUnixMode_StripsHighBitsWithinStModeRange()
    {
        const int mode4755 = 2541;
        Assert.True(FileReplacementPermissions.TryNormalizeUnixMode(mode4755, out var normalized));
        Assert.Equal(493, normalized);
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
