using System.Diagnostics;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

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
    public void WineCommandLineFallback_RequiresClientPathBoundary()
    {
        Assert.True(GameProcessProbe.CommandLineMatches("Z:\\tmp\\client\\Wow.exe\0", "/tmp/client"));
        Assert.False(GameProcessProbe.CommandLineMatches("Z:\\tmp\\client-copy\\Wow.exe\0", "/tmp/client"));
    }
}
