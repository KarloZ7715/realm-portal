using System.Runtime.CompilerServices;
using RealmPortal.Core.ClientFiles;

namespace RealmPortal.Tests.Support;

/// <summary>
/// Points the process-wide default backup directory at a sentinel under the temp path
/// before any test runs. A forgotten per-test override writes here, never into the user folder
/// (<c>~/.local/share/realm-portal/backups</c> on Linux).
/// </summary>
internal static class TestBackupGuard
{
    // One sentinel per process. RealmPortal.App.Tests references this assembly, so both copies of this
    // module initializer run in a single test process: a per-assembly random name would leave the two
    // copies disagreeing about the path the shared environment variable holds.
    internal static readonly string Sentinel = Path.Combine(
        Path.GetTempPath(),
        "realm-portal-test-sentinel-" + Environment.ProcessId);

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Sentinel);
        Environment.SetEnvironmentVariable(ClientFilesApplier.BackupDirectoryVariable, Sentinel);
    }
}

/// <summary>The process default is the sentinel. Any test that reaches it writes there, not under LocalApplicationData.</summary>
public class BackupFolderGuardTests
{
    [Fact]
    public void Guard_NoTestUsesTheDefaultBackupFolder()
    {
        var root = Path.GetFullPath(ClientFilesApplier.DefaultBackupRoot());
        var sentinel = Path.GetFullPath(TestBackupGuard.Sentinel);
        var userDefault = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "realm-portal",
            "backups"));

        // DefaultBackupRoot() with no delegate reads the process environment set by the module initializer.
        // A test that forgets its own backup-dir override writes under this sentinel.
        Assert.Equal(sentinel, root);
        Assert.NotEqual(userDefault, root);
        var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        Assert.StartsWith(tempRoot + Path.DirectorySeparatorChar, root, StringComparison.Ordinal);

        // Nothing may be written under the sentinel: a leftover means a test used the process default instead
        // of its own backup directory. Writes made after this test ran are caught by the copy in the other
        // test assembly, whose guard test observes the sentinel at a different point in the run.
        Assert.Empty(Directory.EnumerateFileSystemEntries(sentinel));
    }
}
