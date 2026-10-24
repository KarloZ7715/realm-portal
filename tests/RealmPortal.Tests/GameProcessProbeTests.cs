using System.Diagnostics;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>Starts real wow/wine-named processes, which every real probe on the machine sees; never run it in parallel.</summary>
[CollectionDefinition(nameof(GameProcessProbeTests), DisableParallelization = true)]
public sealed class GameProcessProbeCollection;

[Collection(nameof(GameProcessProbeTests))]
public sealed class GameProcessProbeTests
{
    [Fact]
    public void ProcessNamedWowExe_IsMatchedOnlyForItsClientDirectory()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new TempDirectory();
        var clientA = temp.Combine("client-a");
        var clientB = temp.Combine("client-b");
        Directory.CreateDirectory(clientA);
        Directory.CreateDirectory(clientB);
        var game = Path.Combine(clientA, "Wow.exe");
        File.Copy("/bin/sleep", game);
        using var process = Process.Start(new ProcessStartInfo(game, "30")
        {
            WorkingDirectory = clientA,
            UseShellExecute = false,
        })!;
        try
        {
            var probe = new GameProcessProbe();
            Assert.True(probe.IsGameRunning(clientA));
            Assert.False(probe.IsGameRunning(clientB));
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    [Fact]
    public void WineLauncherInClientDirectory_IsMatchedDuringStartup()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        Directory.CreateDirectory(client);
        var wine = Path.Combine(client, "wine");
        File.Copy("/bin/sleep", wine);
        using var process = Process.Start(new ProcessStartInfo(wine, "30")
        {
            WorkingDirectory = client,
            UseShellExecute = false,
        })!;
        try
        {
            Assert.True(new GameProcessProbe().IsGameRunning(client));
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    [Fact]
    public void ZombieWineProcessElsewhere_DoesNotBlockOtherClients()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new TempDirectory();
        var elsewhere = temp.Combine("elsewhere");
        var client = temp.Combine("client");
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(client);
        File.Copy("/bin/sleep", Path.Combine(elsewhere, "wine"));
        // The parent shell waits on stdin before reaping, so "wine" stays a zombie meanwhile:
        // listed by name, but /proc/<pid>/cwd is unreadable.
        using var parent = Process.Start(new ProcessStartInfo("/bin/sh")
        {
            ArgumentList = { "-c", "./wine 0 & echo $!; read _; wait" },
            WorkingDirectory = elsewhere,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        })!;
        try
        {
            var zombie = int.Parse(parent.StandardOutput.ReadLine()!);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!GameProcessProbe.StatShowsExited(File.ReadAllText($"/proc/{zombie}/stat")))
            {
                Assert.True(DateTime.UtcNow < deadline, "the child did not become a zombie");
                Thread.Sleep(10);
            }

            Assert.False(new GameProcessProbe().IsGameRunning(client));
        }
        finally
        {
            // Let the shell reap the zombie, so it doesn't outlive the test.
            parent.StandardInput.Close();
            if (!parent.WaitForExit(5000))
                parent.Kill(entireProcessTree: true);
            parent.WaitForExit();
        }
    }

    [Fact]
    public void ReadTaskState_VanishedZombieLiveAndUnreadable()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new TempDirectory();
        var proc = temp.Combine("proc-123");

        // Vanished: /proc/<pid> no longer exists (ENOENT).
        Assert.Equal(GameProcessProbe.TaskState.Gone, GameProcessProbe.ReadTaskState(proc));

        Directory.CreateDirectory(proc);
        var stat = Path.Combine(proc, "stat");
        File.WriteAllText(stat, "123 (wine) Z 1 123 123 0");
        Assert.Equal(GameProcessProbe.TaskState.Exited, GameProcessProbe.ReadTaskState(proc));

        File.WriteAllText(stat, "123 (wine) S 1 123 123 0");
        Assert.Equal(GameProcessProbe.TaskState.Live, GameProcessProbe.ReadTaskState(proc));

        // Permission denied (EACCES) is not "exited": the conservative guard must stay.
        if (Environment.UserName != "root")
        {
            File.SetUnixFileMode(stat, UnixFileMode.None);
            Assert.Equal(GameProcessProbe.TaskState.Unreadable, GameProcessProbe.ReadTaskState(proc));
            Assert.True(GameProcessProbe.LinuxProcessBlocks(proc, temp.Combine("client")));
        }
    }

    /// <summary>Builds a fake <c>/proc/&lt;pid&gt;</c> tree: the leader's stat plus <c>task/&lt;tid&gt;</c> entries.</summary>
    private sealed class FakeProc
    {
        public FakeProc(TempDirectory temp, string leaderState)
        {
            Root = temp.Combine("proc-100");
            Directory.CreateDirectory(Path.Combine(Root, "task"));
            File.WriteAllText(Path.Combine(Root, "stat"), $"100 (wine) {leaderState} 1 100 100 0");
            File.WriteAllText(Path.Combine(Root, "cmdline"), "");
            AddTask(100, leaderState, cwd: null);
        }

        public string Root { get; }

        /// <summary>Adds a thread; <paramref name="cwd"/> null leaves its cwd unreadable, as for a zombie or a denied task.</summary>
        public void AddTask(int tid, string state, string? cwd)
        {
            var task = Path.Combine(Root, "task", tid.ToString());
            Directory.CreateDirectory(task);
            File.WriteAllText(Path.Combine(task, "stat"), $"{tid} (wine) {state} 1 100 100 0");
            File.WriteAllText(Path.Combine(task, "cmdline"), "");
            if (cwd is not null)
                Directory.CreateSymbolicLink(Path.Combine(task, "cwd"), cwd);
        }
    }

    [Fact]
    public void ExitedLeaderWithLiveThreadInClient_IsRunning_ByFolderMatch()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var other = temp.Combine("other");
        Directory.CreateDirectory(client);
        Directory.CreateDirectory(other);
        var proc = new FakeProc(temp, "Z");
        proc.AddTask(101, "S", cwd: client);

        Assert.True(GameProcessProbe.LinuxProcessBlocks(proc.Root, client));
        // Not blind blocking: the live thread's cwd is compared with the client folder.
        Assert.False(GameProcessProbe.LinuxProcessBlocks(proc.Root, other));
    }

    [Fact]
    public void ExitedLeaderWithOnlyZombieOrDeadThreads_IsNotRunning()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        Directory.CreateDirectory(client);
        var proc = new FakeProc(temp, "Z");
        proc.AddTask(101, "Z", cwd: null);
        proc.AddTask(102, "X", cwd: null);

        Assert.False(GameProcessProbe.LinuxProcessBlocks(proc.Root, client));
    }

    [Fact]
    public void ExitedLeaderWithLiveThreadWhoseCwdIsUnreadable_IsRunning()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        Directory.CreateDirectory(client);
        var proc = new FakeProc(temp, "Z");
        proc.AddTask(101, "S", cwd: null);

        Assert.True(GameProcessProbe.LinuxProcessBlocks(proc.Root, client));
    }

    [Fact]
    public void ExitedLeaderWithoutTaskDirectory_IsNotRunning()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new TempDirectory();
        var proc = new FakeProc(temp, "Z");
        Directory.Delete(Path.Combine(proc.Root, "task"), recursive: true);

        Assert.False(GameProcessProbe.LinuxProcessBlocks(proc.Root, temp.Combine("client")));
    }

    [Fact]
    public void RealProcessWhoseMainThreadExited_IsRunningOnlyForItsClient()
    {
        if (!OperatingSystem.IsLinux()) return;
        var compiler = new[] { "/usr/bin/cc", "/usr/bin/gcc" }.FirstOrDefault(File.Exists);
        if (compiler is null)
        {
            // No C compiler on this machine; the fake /proc tests above cover the same decision.
            return;
        }

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var other = temp.Combine("other");
        Directory.CreateDirectory(client);
        Directory.CreateDirectory(other);
        var source = temp.Combine("helper.c");
        // Like Wine's ExitThread on the main thread: the leader exits (state Z), a worker keeps running in the client.
        File.WriteAllText(source, """
            #include <pthread.h>
            #include <unistd.h>
            static void *worker(void *arg) { (void)arg; sleep(30); return 0; }
            int main(int argc, char **argv) {
                pthread_t t;
                if (argc < 2 || chdir(argv[1]) != 0) return 2;
                if (pthread_create(&t, 0, worker, 0) != 0) return 3;
                pthread_exit(0);
            }
            """);
        var wine = temp.Combine("wine");
        using (var cc = Process.Start(new ProcessStartInfo(compiler)
        {
            ArgumentList = { "-pthread", "-o", wine, source },
            UseShellExecute = false,
            RedirectStandardError = true,
        })!)
        {
            var stderr = cc.StandardError.ReadToEnd();
            cc.WaitForExit();
            Assert.True(cc.ExitCode == 0, $"cc failed: {stderr}");
        }

        using var process = Process.Start(new ProcessStartInfo(wine) { ArgumentList = { client }, UseShellExecute = false })!;
        try
        {
            var proc = $"/proc/{process.Id}";
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (GameProcessProbe.ReadTaskState(proc) != GameProcessProbe.TaskState.Exited
                   || Directory.GetDirectories(Path.Combine(proc, "task")).Length < 2)
            {
                Assert.True(DateTime.UtcNow < deadline, "the helper's main thread did not exit");
                Thread.Sleep(10);
            }

            var probe = new GameProcessProbe();
            Assert.True(probe.IsGameRunning(client));
            Assert.False(probe.IsGameRunning(other));
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    [Theory]
    [InlineData("123 (wine) Z 1 123 123 0", true)]
    [InlineData("123 (wine) X 1 123 123 0", true)]
    [InlineData("123 (wine) S 1 123 123 0", false)]
    [InlineData("123 (Wow.exe) R 1 123 123 0", false)]
    [InlineData("123 (a) Z (b) S 1 123", false)]
    [InlineData("garbage", false)]
    public void StatShowsExited_ReadsTheStateAfterTheLastParenthesis(string stat, bool expected) =>
        Assert.Equal(expected, GameProcessProbe.StatShowsExited(stat));

    [Fact]
    public void WineCommandLineFallback_RequiresClientPathBoundary()
    {
        Assert.True(GameProcessProbe.CommandLineMatches("Z:\\tmp\\client\\Wow.exe\0", "/tmp/client"));
        Assert.False(GameProcessProbe.CommandLineMatches("Z:\\tmp\\client-copy\\Wow.exe\0", "/tmp/client"));
    }
}
