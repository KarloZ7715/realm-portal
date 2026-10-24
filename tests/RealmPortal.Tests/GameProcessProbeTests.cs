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
    public void ProcDirectory_VanishedOrZombie_IsExited_ButUnreadableStatKeepsTheGuard()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new TempDirectory();
        var proc = temp.Combine("proc-123");

        // Vanished: /proc/<pid> no longer exists (ENOENT).
        Assert.True(GameProcessProbe.HasExitedOnLinux(proc));

        Directory.CreateDirectory(proc);
        var stat = Path.Combine(proc, "stat");
        File.WriteAllText(stat, "123 (wine) Z 1 123 123 0");
        Assert.True(GameProcessProbe.HasExitedOnLinux(proc));

        File.WriteAllText(stat, "123 (wine) S 1 123 123 0");
        Assert.False(GameProcessProbe.HasExitedOnLinux(proc));

        // Permission denied (EACCES) is not "exited": the conservative guard must stay.
        if (Environment.UserName != "root")
        {
            File.SetUnixFileMode(stat, UnixFileMode.None);
            Assert.False(GameProcessProbe.HasExitedOnLinux(proc));
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
