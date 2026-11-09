using RealmPortal.Core.Updates;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class UnixPathSafetyTests
{
    [Fact]
    public void TryRenameOverwrite_WhenRenameAt2IsMissing_FallsBackToRenameAt()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempDirectory();
        var src = dir.Combine("src.bin");
        var dst = dir.Combine("dst.bin");
        File.WriteAllBytes(src, [1, 2, 3]);
        File.WriteAllBytes(dst, [9, 9, 9]);
        UnixPathSafety.SimulateMissingRenameAt2 = true;
        try
        {
            Assert.True(UnixPathSafety.TryRenameOverwrite(src, dst, out var errno), $"errno={errno}");
            Assert.False(File.Exists(src));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(dst));
        }
        finally
        {
            UnixPathSafety.SimulateMissingRenameAt2 = false;
        }
    }

    [Fact]
    public void TryRenameNoReplace_WhenRenameAt2IsMissing_LinkUnlinkDoesNotOverwrite()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempDirectory();
        var src = dir.Combine("src.bin");
        var dst = dir.Combine("dst.bin");
        File.WriteAllBytes(src, [1, 2, 3]);
        File.WriteAllBytes(dst, [9, 9, 9]);
        UnixPathSafety.SimulateMissingRenameAt2 = true;
        try
        {
            Assert.False(UnixPathSafety.TryRenameNoReplace(src, dst, out var errno));
            Assert.Equal(UnixPathSafety.ErrnoExist, errno);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(src));
            Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(dst));
        }
        finally
        {
            UnixPathSafety.SimulateMissingRenameAt2 = false;
        }
    }

    [UnixFact]
    public void TryResolveSafeDirectory_WhenLastComponentIsASymlink_ReturnsTheRealPath()
    {
        using var dir = new TempDirectory();
        var real = dir.Combine("real");
        var link = dir.Combine("link");
        Directory.CreateDirectory(real);
        Directory.CreateSymbolicLink(link, real);

        Assert.True(UnixPathSafety.TryResolveSafeDirectory(link, out var resolved, out var reason), reason);
        Assert.Equal(UnixPathSafety.RealPath(real), resolved);
        Assert.True(UnixPathSafety.IsDirectoryTreeSafe(link, out reason), reason);
    }
}
