using System.Text;
using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// Issue #149: undo --client-dir must restore only that client's files after interleaved switches.
/// </summary>
public sealed class Issue149UndoPerClientTests
{
    private const string OldW = "set realmlist old-w.example.org\r\n";
    private const string OldX = "set realmlist old-x.example.org\r\n";
    private const string HostA = "a.example.org";
    private const string HostB = "b.example.org";

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }

    private sealed class Box : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public string Config => _dir.Combine("config");
        public string Projection => _dir.Combine("projection");
        public string ClientW => _dir.Combine("client-w");
        public string ClientX => _dir.Combine("client-x");
        public TempDirectory Dir => _dir;

        public Box()
        {
            foreach (var client in new[] { ClientW, ClientX })
            {
                Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
                File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            }
            File.WriteAllBytes(Path.Combine(ClientW, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1.GetBytes(OldW));
            File.WriteAllBytes(Path.Combine(ClientX, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1.GetBytes(OldX));
        }

        public (int Code, string Out, string Err) Run(params string[] args) =>
            RunWithEnv(null, args);

        public (int Code, string Out, string Err) RunWithEnv(Func<string, string?>? extraEnv, params string[] args)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            var variables = extraEnv is null
                ? TestBackupDir.Variables(_dir)
                : TestBackupDir.Variables(_dir, extraEnv);
            var code = Program.Run(
                args, o, e, new PeClientVersionReader(), new FakeLauncher(),
                variables, new NeverRunningGameProbe());
            return (code, o.ToString(), e.ToString());
        }

        public string Read(string client) =>
            File.ReadAllText(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1);

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void Undo_ClientDir_AfterInterleavedSwitches_DoesNotRestoreOtherClientFiles()
    {
        // Acceptance (#149): switch A@W, B@X, A@W, then undo@W must not restore X.
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);
        Assert.Equal(0, box.Run("add", "B", HostB, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);

        Assert.Equal(0, box.Run("switch", "A", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientW).Code);
        Assert.Equal(0, box.Run("switch", "B", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientX).Code);
        Assert.Equal(0, box.Run("switch", "A", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientW).Code);

        var xBeforeUndo = box.Read(box.ClientX);
        Assert.Contains(HostB, xBeforeUndo, StringComparison.Ordinal);

        var undo = box.Run("undo", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientW);
        Assert.Equal(0, undo.Code);
        Assert.Equal(xBeforeUndo, box.Read(box.ClientX));
        Assert.Contains(HostB, box.Read(box.ClientX), StringComparison.Ordinal);
        Assert.Equal(OldW, box.Read(box.ClientW));
    }

    [Fact]
    public void Undo_WithoutClientDir_StillUndoesGlobalLatestSwitch()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);
        Assert.Equal(0, box.Run("add", "B", HostB, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);

        Assert.Equal(0, box.Run("switch", "A", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientW).Code);
        Assert.Equal(0, box.Run("switch", "B", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientX).Code);

        // Global undo (no --client-dir) undoes the latest switch B@X; W must be byte-identical.
        var wBeforeUndo = box.Read(box.ClientW);
        var undo = box.Run("undo", "--config-dir", box.Config, "--projection-dir", box.Projection);
        Assert.Equal(0, undo.Code);
        Assert.Equal(OldX, box.Read(box.ClientX));
        Assert.Equal(wBeforeUndo, box.Read(box.ClientW));
    }

    [Fact]
    public void Undo_ClientDir_UndoesThatClientEvenWhenGlobalLatestIsAnother()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);
        Assert.Equal(0, box.Run("add", "B", HostB, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);

        Assert.Equal(0, box.Run("switch", "A", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientW).Code);
        Assert.Equal(0, box.Run("switch", "B", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientX).Code);

        var xBefore = box.Read(box.ClientX);
        var undo = box.Run("undo", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientW);
        Assert.Equal(0, undo.Code);
        Assert.Equal(xBefore, box.Read(box.ClientX));
        Assert.Equal(OldW, box.Read(box.ClientW));
    }

    [Fact]
    public void Undo_WithoutClientDir_StaysGlobalEvenWhenClientDirEnvIsSet()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);
        Assert.Equal(0, box.Run("add", "B", HostB, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);

        Assert.Equal(0, box.Run("switch", "A", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientW).Code);
        Assert.Equal(0, box.Run("switch", "B", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientX).Code);

        var wBefore = box.Read(box.ClientW);
        // REALM_PORTAL_CLIENT_DIR alone must not scope undo — still undoes global latest (B@X).
        var undo = box.RunWithEnv(
            name => name == CliApp.ClientDirVariable ? box.ClientW : null,
            "undo", "--config-dir", box.Config, "--projection-dir", box.Projection);
        Assert.Equal(0, undo.Code);
        Assert.Equal(OldX, box.Read(box.ClientX));
        Assert.Equal(wBefore, box.Read(box.ClientW));
    }

    [Fact]
    public void Undo_ClientDir_WorksWhenWowExeIsMissing()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);
        Assert.Equal(0, box.Run("add", "B", HostB, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);

        Assert.Equal(0, box.Run("switch", "A", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientW).Code);
        Assert.Equal(0, box.Run("switch", "B", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientX).Code);

        File.Delete(Path.Combine(box.ClientW, "Wow.exe"));

        var xBefore = box.Read(box.ClientX);
        var undo = box.Run("undo", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientW);
        Assert.Equal(0, undo.Code);
        Assert.Equal(OldW, box.Read(box.ClientW));
        Assert.Equal(xBefore, box.Read(box.ClientX));
    }

    [Fact]
    public void Undo_BlankClientDirOption_IsUsageErrorAndDoesNotUseEnv()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", HostA, "--config-dir", box.Config, "--projection-dir", box.Projection).Code);
        Assert.Equal(0, box.Run("switch", "A", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.ClientW).Code);

        var wBefore = box.Read(box.ClientW);
        var undo = box.RunWithEnv(
            name => name == CliApp.ClientDirVariable ? box.ClientW : null,
            "undo", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", "");
        Assert.Equal(1, undo.Code);
        Assert.Contains("--client-dir", undo.Err, StringComparison.Ordinal);
        Assert.Equal(wBefore, box.Read(box.ClientW));
    }
}
