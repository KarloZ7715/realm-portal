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
}
