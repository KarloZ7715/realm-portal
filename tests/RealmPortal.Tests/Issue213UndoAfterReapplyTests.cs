using System.Text;
using System.Text.Json;
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
    private const int NothingToUndoExitCode = 2;

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }

    private sealed class Box : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public string Client => _dir.Combine("client");

        public string OtherClient => _dir.Combine("other-client");

        public Box()
        {
            foreach (var client in new[] { Client, OtherClient })
            {
                Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
                File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
                File.WriteAllBytes(RealmlistOf(client), Encoding.ASCII.GetBytes(Original));
            }
        }

        public string RealmlistPath => RealmlistOf(Client);

        public string JournalPath => _dir.Combine(Path.Combine("config", "last-switch.json"));

        public IEnumerable<string> Manifests() =>
            Directory.EnumerateFiles(_dir.Combine("backups"), "manifest.json", SearchOption.AllDirectories);

        public string ClientIdOf(string clientDir) =>
            Run("client", "list").Out.Split('\n')
                .Single(line => line.Contains(clientDir, StringComparison.Ordinal))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        /// <summary>Client ids with a pending undo in <c>state.json</c>.</summary>
        public IReadOnlyList<string> UndoByClientIds() =>
            KeysOf(_dir.Combine(Path.Combine("config", "state.json")), "undo_by_client");

        /// <summary>Client ids with an entry in <c>last-switch.json</c>.</summary>
        public IReadOnlyList<string> JournalClientIds() =>
            File.Exists(JournalPath) ? KeysOf(JournalPath, "by_client") : [];

        private static IReadOnlyList<string> KeysOf(string jsonPath, string property)
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
            return doc.RootElement.TryGetProperty(property, out var map) && map.ValueKind == JsonValueKind.Object
                ? map.EnumerateObject().Select(p => p.Name).ToList()
                : [];
        }

        private static string RealmlistOf(string client) => Path.Combine(client, "Data", "enUS", "realmlist.wtf");

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

        // The dropped undo leaves no journal entry behind.
        Assert.False(File.Exists(box.JournalPath));

        var undo = box.Run("undo", "--client-dir", box.Client);

        Assert.Equal(NothingToUndoExitCode, undo.Code);
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

    [Fact]
    public void Reapply_WithACorruptPendingManifest_StillSucceeds_AndDropsTheUndo()
    {
        // The undo check runs after the files were written and A activated, so it must not turn the switch into an error.
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);
        Assert.Equal(0, box.Run("edit", "A", "--host", NewHostA).Code);
        foreach (var manifest in box.Manifests())
            File.WriteAllText(manifest, "{broken");

        var reapply = box.Run("switch", "A", "--client-dir", box.Client);

        Assert.Equal(0, reapply.Code);
        Assert.Equal(string.Empty, reapply.Err);
        Assert.Contains(NewHostA, box.Realmlist(), StringComparison.Ordinal);
        var status = box.Run("status", "--client-dir", box.Client).Out;
        Assert.Contains("Active server: A", status, StringComparison.Ordinal);
        Assert.Contains("Can undo: no", status, StringComparison.Ordinal);
        var undo = box.Run("undo", "--client-dir", box.Client);
        Assert.Equal(NothingToUndoExitCode, undo.Code);
        Assert.Contains("nothing to undo", undo.Err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reapply_DropsOnlyThisClientsUndo_WhenAnotherClientOwnsTheLatestSwitch()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA).Code);
        Assert.Equal(0, box.Run("add", "B", HostB).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);
        Assert.Equal(0, box.Run("switch", "B", "--client-dir", box.OtherClient).Code);
        Assert.Equal(0, box.Run("edit", "A", "--host", NewHostA).Code);
        Assert.Equal(0, box.Run("switch", "A", "--client-dir", box.Client).Code);

        // This client's undo is gone (state and journal)...
        var clientId = box.ClientIdOf(box.Client);
        var otherClientId = box.ClientIdOf(box.OtherClient);
        Assert.DoesNotContain(clientId, box.UndoByClientIds());
        Assert.Contains(otherClientId, box.UndoByClientIds());
        Assert.DoesNotContain(clientId, box.JournalClientIds());
        var undo = box.Run("undo", "--client-dir", box.Client);
        Assert.Equal(NothingToUndoExitCode, undo.Code);
        Assert.Contains("nothing to undo", undo.Err, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(NewHostA, box.Realmlist(), StringComparison.Ordinal);

        // ...but the latest switch, which belongs to the other client, can still be undone globally.
        var globalUndo = box.Run("undo");
        Assert.Equal(0, globalUndo.Code);
        Assert.Contains("restored 'B'", globalUndo.Out, StringComparison.Ordinal);
        Assert.Equal(Original, File.ReadAllText(Path.Combine(box.OtherClient, "Data", "enUS", "realmlist.wtf"), Encoding.ASCII));
    }
}
