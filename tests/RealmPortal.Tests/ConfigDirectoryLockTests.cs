using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ConfigDirectoryLockTests
{
    [Fact]
    public void Acquire_WhileTheLockFileIsHeldElsewhere_TimesOutAsLockedStoreException()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        using var foreign = HoldLockFile(config);

        var ex = Assert.Throws<StoreException>(() => new ConfigDirectoryLock(config, ShortTimeout).Acquire().Dispose());

        Assert.True(ex.Locked);
        Assert.Equal(ServiceErrorCode.StoreLocked, ServiceErrors.FromException(ex).Code);
    }

    [Fact]
    public void Acquire_AfterTheOtherHolderReleases_Succeeds()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var foreign = HoldLockFile(config);
        Assert.Throws<StoreException>(() => new ConfigDirectoryLock(config, ShortTimeout).Acquire().Dispose());
        foreign.Dispose();

        using var handle = new ConfigDirectoryLock(config).Acquire();

        Assert.NotNull(handle);
        Assert.True(File.Exists(Path.Combine(config, ConfigDirectoryLock.LockFileName)));
    }

    [Fact]
    public void Acquire_FromAnotherThreadOfTheSameProcess_UsesTheInProcessGate()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var held = new ConfigDirectoryLock(config).Acquire();
            acquired.Set();
            release.Wait();
        });

        holder.Start();
        try
        {
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));

            // The gate is what makes the second thread wait; a plain file-lock timeout would carry an IOException.
            var ex = Assert.Throws<StoreException>(() => new ConfigDirectoryLock(config, ShortTimeout).Acquire().Dispose());

            Assert.True(ex.Locked);
            Assert.Null(ex.InnerException);
        }
        finally
        {
            release.Set();
            holder.Join();
        }
    }

    [Fact]
    public void Acquire_FromAnotherThread_SucceedsOnceTheHolderDisposes()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var first = new ConfigDirectoryLock(config).Acquire();
        var acquired = false;
        var second = new Thread(() =>
        {
            using var handle = new ConfigDirectoryLock(config).Acquire();
            acquired = true;
        });

        second.Start();
        Assert.False(acquired);
        first.Dispose();
        Assert.True(second.Join(TimeSpan.FromSeconds(5)));
        Assert.True(acquired);
    }

    [Fact]
    public void Acquire_OnTheSameThread_IsReentrant()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var lockA = new ConfigDirectoryLock(config);
        var lockB = new ConfigDirectoryLock(config); // a second instance of the same directory

        using var outer = lockA.Acquire();
        using var inner = lockB.Acquire();

        Assert.NotNull(inner);
    }

    [Fact]
    public void Acquire_WhenTheConfigPathIsAFile_StillRaisesStoreException()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        File.WriteAllText(config, "not a directory");

        var ex = Assert.Throws<StoreException>(() => new ConfigDirectoryLock(config).Acquire().Dispose());

        Assert.False(ex.Locked);
        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public void Acquire_WhenOpeningFailsForAReasonOtherThanContention_ReportsNotLocked()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        // The lock path exists as a directory: opening it fails without any other process being involved.
        Directory.CreateDirectory(Path.Combine(config, ConfigDirectoryLock.LockFileName));

        var ex = Assert.Throws<StoreException>(() => new ConfigDirectoryLock(config).Acquire().Dispose());

        Assert.False(ex.Locked);
        Assert.IsType<UnauthorizedAccessException>(ex.InnerException);
        Assert.NotEqual(ServiceErrorCode.StoreLocked, ServiceErrors.FromException(ex).Code);
    }

    [Fact]
    public void Add_WhileTheLockFileIsHeldElsewhere_ChangesNothing()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        using var foreign = HoldLockFile(config);
        var store = new ServerStore(config, new AtomicFileWriter(), new ConfigDirectoryLock(config, ShortTimeout));

        var ex = Assert.Throws<StoreException>(() => store.Add(Server.Create("Alpha", "a.example.org")));

        Assert.True(ex.Locked);
        Assert.False(File.Exists(store.FilePath));
    }

    private static TimeSpan ShortTimeout => TimeSpan.FromMilliseconds(200);

    /// <summary>Opens the lock file the way another Realm Portal process would: exclusive, no sharing.</summary>
    private static FileStream HoldLockFile(string config)
    {
        Directory.CreateDirectory(config);
        return new FileStream(
            Path.Combine(config, ConfigDirectoryLock.LockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
    }
}
