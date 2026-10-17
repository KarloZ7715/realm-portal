using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class AtomicFileWriterTests
{
    [Fact]
    public void Writes_NewFile_CreatingDirectories()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("a", "b", "file.bin");

        new AtomicFileWriter().WriteAllBytes(path, [1, 2, 3]);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    public void Replaces_ExistingFile_AndLeavesNoTempFiles()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("file.bin");
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        new AtomicFileWriter().WriteAllBytes(path, [1]);

        Assert.Equal([1], File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void OnFailure_KeepsOldContent_AndLeavesNoTempFiles()
    {
        using var dir = new TempDirectory();
        // The target is a directory, so the final rename must fail.
        var path = dir.Combine("target");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "keep.txt"), "x");

        Assert.ThrowsAny<Exception>(() => new AtomicFileWriter().WriteAllBytes(path, [1]));

        Assert.True(File.Exists(Path.Combine(path, "keep.txt")));
        Assert.Equal([path], Directory.GetFileSystemEntries(dir.Path));
    }
}
