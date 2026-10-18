using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ActiveServerManagerTests
{
    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public string Config => _dir.Combine("config");
        public string Projection => _dir.Combine("wow-servidores");
        public string ActivoPath => ActiveProjection.FilePath(Projection);
        public ServerStore Store => new(Config, new AtomicFileWriter());
        public ActiveServerManager Manager => new(Store, Config, Projection, new AtomicFileWriter());

        public Server Add(string name, string? realm = null) =>
            Store.Add(Server.Create(name, name.ToLowerInvariant() + ".example.org", realm));

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void Fresh_HasNoActiveServer_AndWritesNothing()
    {
        using var env = new Env();

        var status = env.Manager.Status();

        Assert.Null(status.Active);
        Assert.True(status.ProjectionInSync);
        Assert.False(status.CanUndo);
        Assert.False(Directory.Exists(env.Projection));
    }

    [Fact]
    public void Switch_WritesActivoJson_AndIsRememberedAcrossSessions()
    {
        using var env = new Env();
        var a = env.Add("Alpha", "R1");

        env.Manager.Switch(a);

        Assert.Equal(ActiveProjection.Serialize(a), File.ReadAllBytes(env.ActivoPath));
        var newSession = env.Manager; // new instances read state.json
        Assert.Equal(a, newSession.GetActive());
        Assert.True(newSession.Status().ProjectionInSync);
    }

    [Fact]
    public void Switch_ThenUndo_RestoresPrevious_ThenNothingToUndo()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        var b = env.Add("Beta");
        env.Manager.Switch(a);
        env.Manager.Switch(b);
        Assert.Equal(ActiveProjection.Serialize(b), File.ReadAllBytes(env.ActivoPath));

        var restored = env.Manager.Undo();

        Assert.Equal(a, restored);
        Assert.Equal(a, env.Manager.GetActive());
        Assert.Equal(ActiveProjection.Serialize(a), File.ReadAllBytes(env.ActivoPath));
        Assert.Throws<StoreException>(() => env.Manager.Undo());
    }

    [Fact]
    public void Undo_OfFirstSwitch_DeactivatesAndDeletesActivoJson()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        env.Manager.Switch(a);

        Assert.Null(env.Manager.Undo());

        Assert.Null(env.Manager.GetActive());
        Assert.False(File.Exists(env.ActivoPath));
    }

    [Fact]
    public void Undo_ToNoActiveServer_WhenActivoDeleteFails_KeepsStateAndActivo()
    {
        // A read-only directory does not stop root from unlinking activo.json.
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess)
            return;

        using var env = new Env();
        var a = env.Add("Alpha");
        env.Manager.Switch(a);

        var statePath = Path.Combine(env.Config, ActiveServerManager.StateFileName);
        var stateBefore = File.ReadAllBytes(statePath);
        var activoBefore = File.ReadAllBytes(env.ActivoPath);
        Assert.True(env.Manager.Status().CanUndo);

        var mode = File.GetUnixFileMode(env.Projection);
        File.SetUnixFileMode(env.Projection, mode & ~UnixFileMode.UserWrite & ~UnixFileMode.GroupWrite & ~UnixFileMode.OtherWrite);
        try
        {
            Assert.Throws<StoreException>(() => env.Manager.Undo());

            Assert.Equal(stateBefore, File.ReadAllBytes(statePath));
            Assert.Equal(activoBefore, File.ReadAllBytes(env.ActivoPath));
            Assert.Equal(a, env.Manager.GetActive());
            Assert.True(env.Manager.Status().CanUndo);
        }
        finally
        {
            File.SetUnixFileMode(env.Projection, mode);
        }
    }

    [Fact]
    public void Undo_WithoutSwitch_Throws()
    {
        using var env = new Env();
        Assert.Throws<StoreException>(() => env.Manager.Undo());
    }

    [Fact]
    public void SwitchToAlreadyActive_KeepsUndoTarget()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        var b = env.Add("Beta");
        env.Manager.Switch(a);
        env.Manager.Switch(b);

        env.Manager.Switch(b);

        Assert.Equal(a, env.Manager.Undo());
    }

    [Fact]
    public void Switch_ToServerNotInStore_Throws_AndChangesNothing()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        env.Manager.Switch(a);

        Assert.Throws<StoreException>(() => env.Manager.Switch(Server.Create("Ghost", "g")));

        Assert.Equal(a, env.Manager.GetActive());
        Assert.Equal(ActiveProjection.Serialize(a), File.ReadAllBytes(env.ActivoPath));
    }

    [Fact]
    public void RemoveActive_Deactivates_DeletesActivoJson_AndClearsUndo()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        var b = env.Add("Beta");
        env.Manager.Switch(a);
        env.Manager.Switch(b);

        Assert.True(env.Manager.Remove(b.Id));

        var status = env.Manager.Status();
        Assert.Null(status.Active);
        Assert.False(status.CanUndo);
        Assert.False(File.Exists(env.ActivoPath));
        Assert.Equal([a], env.Store.List());
    }

    [Fact]
    public void RemoveUndoTarget_DropsUndo_ButKeepsActive()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        var b = env.Add("Beta");
        env.Manager.Switch(a);
        env.Manager.Switch(b);

        env.Manager.Remove(a.Id);

        Assert.Equal(b, env.Manager.GetActive());
        Assert.False(env.Manager.Status().CanUndo);
    }

    [Fact]
    public void Remove_Unknown_ReturnsFalse()
    {
        using var env = new Env();
        Assert.False(env.Manager.Remove("nope"));
    }

    [Fact]
    public void DiscardPendingUndo_WhileTheConfigLockIsHeldElsewhere_WaitsBeforeChangingState()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        var b = env.Add("Beta");
        var manager = env.Manager;
        manager.Switch(a);
        manager.Switch(b);
        Assert.True(manager.Status().CanUndo);

        Directory.CreateDirectory(env.Config);
        using var foreign = new FileStream(
            Path.Combine(env.Config, ConfigDirectoryLock.LockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        var call = Task.Run(() => manager.DiscardPendingUndo());

        Assert.False(call.Wait(TimeSpan.FromMilliseconds(400)), "discard must take the config lock before it reads the state");
        Assert.True(manager.Status().CanUndo, "nothing may be written before the lock is granted");

        foreign.Dispose();

        Assert.True(call.Wait(TimeSpan.FromSeconds(15)));
        Assert.False(manager.Status().CanUndo);
        Assert.Equal(b, manager.GetActive());
    }

    [Fact]
    public void Status_DetectsTamperedProjection()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        env.Manager.Switch(a);
        File.WriteAllText(env.ActivoPath, "{}");

        Assert.False(env.Manager.Status().ProjectionInSync);

        env.Manager.Switch(a); // re-switching repairs it
        Assert.True(env.Manager.Status().ProjectionInSync);
    }

    [Fact]
    public void Status_OversizedProjectionIsNotInSync()
    {
        using var env = new Env();
        var active = env.Add("Alpha");
        env.Manager.Switch(active);
        using (var file = new FileStream(env.ActivoPath, FileMode.Open, FileAccess.Write, FileShare.Read))
            file.SetLength(PrivateStorage.MaxJsonBytes + 1L);

        var status = env.Manager.Status();

        Assert.False(status.ProjectionInSync);
    }

    [Fact]
    public void CorruptState_Throws_AndIsNotOverwritten()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        File.WriteAllText(Path.Combine(env.Config, ActiveServerManager.StateFileName), "garbage");

        Assert.Throws<StoreException>(() => env.Manager.Switch(a));

        Assert.Equal("garbage", File.ReadAllText(Path.Combine(env.Config, ActiveServerManager.StateFileName)));
        Assert.False(File.Exists(env.ActivoPath));
    }

    [Property(MaxTest = 60)]
    public Property Undo_AfterAnySwitchSequence_RestoresTheServerBeforeTheLastChange() =>
        Prop.ForAll(
            Gen.Choose(0, 2).ListOf().Where(l => l.Count > 0).ToArbitrary(),
            picks =>
            {
                using var env = new Env();
                var servers = new[] { env.Add("Alpha"), env.Add("Beta"), env.Add("Gamma") };
                var manager = env.Manager;
                Server? beforeLastChange = null;
                Server? current = null;
                foreach (var pick in picks)
                {
                    var target = servers[pick];
                    if (current?.Id != target.Id)
                        beforeLastChange = current;
                    manager.Switch(target);
                    current = target;
                }

                var restored = manager.Undo();
                var activoMatches = restored is null
                    ? !File.Exists(env.ActivoPath)
                    : File.ReadAllBytes(env.ActivoPath).SequenceEqual(ActiveProjection.Serialize(restored));
                return restored == beforeLastChange && manager.GetActive() == beforeLastChange && activoMatches;
            });

    private static void OverwriteRealmlist(Env env, string serverId, string badRealmlist)
    {
        // Simulates a hand-edited servers.json.
        var path = env.Store.FilePath;
        var json = File.ReadAllText(path);
        var good = System.Text.Json.JsonSerializer.Serialize(env.Store.List().Single(s => s.Id == serverId).Realmlist);
        var bad = System.Text.Json.JsonSerializer.Serialize(badRealmlist);
        Assert.Contains(good[1..^1], json);
        File.WriteAllText(path, json.Replace(good[1..^1], bad[1..^1]));
    }

    [Theory]
    [InlineData("SET REALMLIST h.example.org\r\n", InvalidRealmlistKind.WrongFirstLine)]
    [InlineData("set realmlist h\u00f3st.example.org\r\n", InvalidRealmlistKind.NotAscii)]
    [InlineData("set realmlist h.example.org\r\nset portal p.example.org\r\n", InvalidRealmlistKind.ForbiddenKey)]
    [InlineData("set realmlist h.example.org\r\nset realmname Foo\r\n", InvalidRealmlistKind.ForbiddenKey)]
    [InlineData("set realmlist h.example.org\r\nhello\r\n", InvalidRealmlistKind.MalformedLine)]
    [InlineData("set realmlist h.example.org\r\nset patchlist a b\r\n", InvalidRealmlistKind.InvalidValue)]
    [InlineData("set realmlist h.example.org\r\nset patchlist \\\"x\\\"\r\n", InvalidRealmlistKind.InvalidValue)]
    [InlineData("set realmlist h.example.org\n", InvalidRealmlistKind.BadLineEnding)]
    public void Switch_ToServerWithInvalidRealmlist_RejectsCorruptStore_AndWritesNothing(string badRealmlist, InvalidRealmlistKind kind)
    {
        using var env = new Env();
        var good = env.Add("Good");
        var bad = env.Add("Bad");
        env.Manager.Switch(good);
        var activoBefore = File.ReadAllBytes(env.ActivoPath);
        var statePath = Path.Combine(env.Config, ActiveServerManager.StateFileName);
        var stateBefore = File.ReadAllBytes(statePath);
        OverwriteRealmlist(env, bad.Id, badRealmlist);
        var storeBefore = File.ReadAllBytes(env.Store.FilePath);

        var ex = Assert.Throws<StoreException>(() => env.Manager.Switch(env.Store.Find("Bad")!));

        Assert.Contains("stored server is invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"[{kind}]", ex.InnerException?.Message ?? ex.Message);
        Assert.Equal(activoBefore, File.ReadAllBytes(env.ActivoPath));
        Assert.Equal(stateBefore, File.ReadAllBytes(statePath));
        Assert.Equal(storeBefore, File.ReadAllBytes(env.Store.FilePath));
    }

    [Fact]
    public void Switch_ToInvalidServerWhenNothingIsActive_CreatesNoActivoJsonAndNoState()
    {
        using var env = new Env();
        var bad = env.Add("Bad");
        OverwriteRealmlist(env, bad.Id, "SET REALMLIST h\r\n");

        Assert.Throws<StoreException>(() => env.Manager.Switch(env.Store.Find("Bad")!));

        Assert.False(File.Exists(env.ActivoPath));
        Assert.False(File.Exists(Path.Combine(env.Config, ActiveServerManager.StateFileName)));
    }

    [Fact]
    public void Undo_ToServerWithInvalidRealmlist_Throws_AndChangesNothing()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        var b = env.Add("Beta");
        env.Manager.Switch(a);
        env.Manager.Switch(b);
        var activoBefore = File.ReadAllBytes(env.ActivoPath);
        var statePath = Path.Combine(env.Config, ActiveServerManager.StateFileName);
        var stateBefore = File.ReadAllBytes(statePath);
        OverwriteRealmlist(env, a.Id, "set realmlist caf\u00e9.example.org\r\n");

        Assert.Throws<StoreException>(() => env.Manager.Undo());

        Assert.Equal(activoBefore, File.ReadAllBytes(env.ActivoPath));
        Assert.Equal(stateBefore, File.ReadAllBytes(statePath));
    }

    [Fact]
    public void Status_WithInvalidStoredServer_RejectsCorruptStore()
    {
        using var env = new Env();
        var a = env.Add("Alpha");
        env.Manager.Switch(a);
        OverwriteRealmlist(env, a.Id, "SET REALMLIST h\r\n");

        Assert.Throws<StoreException>(() => env.Manager.Status());
    }

    [Fact]
    public void Switch_KeepsProjectionInSync()
    {
        using var env = new Env();
        var store = new ServerStore(env.Config, new AtomicFileWriter());
        var manager = new ActiveServerManager(store, env.Config, env.Projection, new AtomicFileWriter());
        var servers = Enumerable.Range(1, 6)
            .Select(i => store.Add(Server.Create($"P{i}", $"p{i}.example.org")))
            .ToList();

        for (var round = 0; round < 8; round++)
        {
            Parallel.ForEach(servers, s => manager.Switch(s));
            var status = manager.Status();
            Assert.True(status.ProjectionInSync);
            Assert.NotNull(status.Active);
            Assert.Equal(status.Active!.Id, manager.GetActive()!.Id);
        }
    }
}
