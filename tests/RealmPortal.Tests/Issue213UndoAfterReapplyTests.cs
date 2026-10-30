using System.Text;
using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// Issue #213: switch A, edit A --host, switch A (re-apply), undo must not report a false conflict.
/// </summary>
public sealed class Issue213UndoAfterReapplyTests
{
    private const string Original = "set realmlist original.example.test\r\n";
    private const string HostA = "a.example.test";
    private const string NewHostA = "a-new.example.test";
    private const string HostB = "b.example.test";
    private const string ConflictMessage = "changed since the switch";

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }

    private sealed class Box : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public string Client => _dir.Combine("client");

        public Box()
        {
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            File.WriteAllBytes(RealmlistPath, Encoding.ASCII.GetBytes(Original));
        }

        public string RealmlistPath => Path.Combine(Client, "Data", "enUS", "realmlist.wtf");

        public (int Code, string Out, string Err) Run(params string[] args)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            string[] common = ["--config-dir", _dir.Combine("config"), "--projection-dir", _dir.Combine("projection")];
            var code = Program.Run(
                [.. args, .. common], o, e, new PeClientVersionReader(), new FakeLauncher(),
                TestBackupDir.Variables(_dir), new NeverRunningGameProbe());
            return (code, o.ToString(), e.ToString());
        }

        public string Realmlist() => File.ReadAllText(RealmlistPath, Encoding.ASCII);

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void Switch_Edit_Reapply_Undo_DoesNotReportAFalseConflict()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);
        Assert.Equal(0, box.Run("edit", "A", "--host", NewHostA).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);
        Assert.Contains(NewHostA, box.Realmlist(), StringComparison.Ordinal);

        var undo = box.Run("undo", "--client-dir", box.Client);

        Assert.DoesNotContain(ConflictMessage, undo.Err, StringComparison.Ordinal);
        Assert.Contains("nothing to undo", undo.Err, StringComparison.OrdinalIgnoreCase);
        // The re-applied files stay as they are.
        Assert.Contains(NewHostA, box.Realmlist(), StringComparison.Ordinal);
        Assert.Contains("Can undo: no", box.Run("status", "--client-dir", box.Client).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Switch_Edit_Reapply_GlobalUndo_DoesNotReportAFalseConflict()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "B", HostB).Code);
        Assert.Equal(0, box.Run("add", "A", HostA).Code);
        Assert.Equal(0, box.Run("switch", "B", "--client-dir", box.Client).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);
        Assert.Equal(0, box.Run("edit", "A", "--host", NewHostA).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);

        var undo = box.Run("undo");

        Assert.DoesNotContain(ConflictMessage, undo.Err, StringComparison.Ordinal);
        Assert.Contains("nothing to undo", undo.Err, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(NewHostA, box.Realmlist(), StringComparison.Ordinal);
        var status = box.Run("status").Out;
        Assert.Contains("Active server: A", status, StringComparison.Ordinal);
        Assert.Contains("Can undo: no", status, StringComparison.Ordinal);
    }

    [Fact]
    public void Switch_Reapply_WithoutChanges_KeepsTheUndo()
    {
        // Re-applying an unchanged server writes nothing, so the first switch can still be undone.
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);

        Assert.Equal(0, box.Run("undo", "--client-dir", box.Client).Code);
        Assert.Equal(Original, box.Realmlist());
    }

    [Fact]
    public void Switch_RepairDriftedFiles_KeepsTheUndo()
    {
        // A re-apply that only repairs drift writes the same bytes as the first switch, so its undo stays valid.
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);
        var applied = box.Realmlist();
        File.WriteAllBytes(box.RealmlistPath, Encoding.ASCII.GetBytes("set realmlist drifted.example.test\r\n"));
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);
        Assert.Equal(applied, box.Realmlist());

        Assert.Equal(0, box.Run("undo", "--client-dir", box.Client).Code);
        Assert.Equal(Original, box.Realmlist());
    }

    [Fact]
    public void Switch_Edit_Reapply_ThenSwitchingAway_CanBeUndoneAgain()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA).Code);
        Assert.Equal(0, box.Run("add", "B", HostB).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);
        Assert.Equal(0, box.Run("edit", "A", "--host", NewHostA).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);
        var reapplied = box.Realmlist();
        Assert.Equal(0, box.Run("switch", "B", "--client-dir", box.Client).Code);

        Assert.Equal(0, box.Run("undo", "--client-dir", box.Client).Code);
        Assert.Equal(reapplied, box.Realmlist());
        Assert.Contains("Active server: A", box.Run("status", "--client-dir", box.Client).Out, StringComparison.Ordinal);
    }
}
