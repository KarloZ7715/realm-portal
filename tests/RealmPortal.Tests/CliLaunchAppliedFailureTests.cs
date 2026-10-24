using System.ComponentModel;
using RealmPortal.Cli;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class CliLaunchAppliedFailureTests
{
    [Fact]
    public void Launch_WhenStartFailsAfterApply_PrintsUndoHintAndKeepsExitCode()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var client = dir.Combine("client");
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(client);
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        string? GetVariable(string name) =>
            name == ClientFilesApplier.BackupDirectoryVariable ? backups : null;

        (int Code, string Out, string Err) Run(string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var code = Program.Run(
                args,
                stdout,
                stderr,
                new PeClientVersionReader(),
                new ThrowingLauncher(),
                GetVariable,
                new NeverRunningGameProbe());
            return (code, stdout.ToString(), stderr.ToString());
        }

        string[] common = ["--config-dir", config, "--projection-dir", projection, "--client-dir", client];
        Assert.Equal(0, Run(["add", "Alpha", "alpha.example.org", .. common]).Code);
        Assert.Equal(0, Run(["switch", "Alpha", .. common]).Code);
        Assert.Equal(0, Run(["add", "Frost", "frost.example.org", .. common]).Code);

        // Named server: launch switches first, then the start fails with the switch kept.
        var named = Run(["launch", "Frost", .. common]);
        Assert.Equal(2, named.Code);
        Assert.Contains("error: Injected process start failure.", named.Err);
        Assert.Contains(
            "The game did not start, but 'Frost' is still applied to this client. Run 'undo' to restore the previous server.",
            named.Err);
        Assert.DoesNotContain("still applied", named.Out);

        // Active server, no argument: the same launch step and the same exit code.
        var active = Run(["launch", .. common]);
        Assert.Equal(2, active.Code);
        Assert.Contains("error: Injected process start failure.", active.Err);
        Assert.Contains(
            "The game did not start, but 'Frost' is still applied to this client. Run 'undo' to restore the previous server.",
            active.Err);
        Assert.DoesNotContain("still applied", active.Out);

        // A launch that never applies does not offer undo.
        Assert.Equal(0, Run(["add", "Classic", "classic.example.org", "--build", "5875", .. common]).Code);
        var refused = Run(["launch", "Classic", .. common]);
        Assert.Equal(2, refused.Code);
        Assert.DoesNotContain("still applied", refused.Err);
        Assert.DoesNotContain("Run 'undo'", refused.Err);
    }

    [Fact]
    public void Launch_WhenStartFailsAfterApplyButUndoWasDiscarded_OmitsUndoHint()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var client = dir.Combine("client");
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(client);
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        string? GetVariable(string name) =>
            name == ClientFilesApplier.BackupDirectoryVariable ? backups : null;

        (int Code, string Out, string Err) Run(string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var code = Program.Run(
                args,
                stdout,
                stderr,
                new PeClientVersionReader(),
                new ThrowingLauncher(),
                GetVariable,
                new NeverRunningGameProbe());
            return (code, stdout.ToString(), stderr.ToString());
        }

        string[] common = ["--config-dir", config, "--projection-dir", projection, "--client-dir", client];
        Assert.Equal(0, Run(["add", "Frost", "frost.example.org", .. common]).Code);
        Assert.Equal(0, Run(["switch", "Frost", .. common]).Code);

        // The switch is already applied. Drop the journal so a later launch of the same server
        // fails after apply without creating a new undo.
        var service = new PortalService(
            config,
            projection,
            new PeClientVersionReader(),
            new NoLaunch(),
            getVariable: GetVariable);
        Assert.True(service.DiscardUndo().Succeeded);
        Assert.False(service.GetStatus().Value!.CanUndo);

        var failed = Run(["launch", "Frost", .. common]);
        Assert.Equal(2, failed.Code);
        Assert.Contains("error: Injected process start failure.", failed.Err);
        Assert.DoesNotContain("still applied", failed.Err);
        Assert.DoesNotContain("Run 'undo'", failed.Err);
        Assert.DoesNotContain("still applied", failed.Out);

        var after = new PortalService(
            config,
            projection,
            new PeClientVersionReader(),
            new NoLaunch(),
            getVariable: GetVariable);
        var status = after.GetStatus();
        Assert.True(status.Succeeded);
        Assert.Equal("Frost", status.Value!.Active!.Name);
        Assert.False(status.Value.CanUndo);
    }

    /// <summary>Same shape as the service test: the adapter wraps this in <see cref="InvalidOperationException"/>.</summary>
    private sealed class ThrowingLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) =>
            throw new Win32Exception("Injected process start failure.");
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("not used");
    }
}
