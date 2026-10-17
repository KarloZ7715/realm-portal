using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>Fault injection in the middle of a write, through the stream seam of <see cref="AtomicFileWriter"/>.</summary>
public class AtomicFileWriterFaultTests
{
    private static readonly Gen<byte[]> BytesGen = Gen.Elements<byte>(0, 1, 13, 10, 65, 200, 255).ArrayOf();
    private static readonly Gen<byte[]?> OptionalBytesGen = Gen.OneOf(Gen.Constant<byte[]?>(null), BytesGen.Select(b => (byte[]?)b));

    [Fact]
    public void WithoutAFault_TheSeamChangesNothing()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("file.bin");
        var fault = new WriteFault(failOnWrite: 99, allowedBytes: 0);

        fault.Writer().WriteAllBytes(path, [1, 2, 3]);

        Assert.False(fault.Fired);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(dir.Path));
    }

    [Property(MaxTest = 300)]
    public Property MidWriteFailure_NeverLeavesAHalfWrittenTarget_AndCleansTheTempFile() =>
        Prop.ForAll(OptionalBytesGen.ToArbitrary(), BytesGen.ToArbitrary(), Gen.Choose(0, 40).ToArbitrary(), (old, content, allowed) =>
        {
            using var dir = new TempDirectory();
            var path = dir.Combine("sub", "file.bin");
            if (old is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, old);
            }

            var fault = new WriteFault(1, allowed);
            var threw = false;
            try
            {
                fault.Writer().WriteAllBytes(path, content);
            }
            catch (IOException)
            {
                threw = true;
            }

            var now = File.Exists(path) ? File.ReadAllBytes(path) : null;
            var oldOrNew = threw
                ? (old is null ? now is null : now is not null && now.SequenceEqual(old))
                : now is not null && now.SequenceEqual(content);
            var leftovers = Directory.Exists(Path.GetDirectoryName(path)!)
                ? Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!).Count(e => e != path)
                : 0;
            return (threw == (content.Length > allowed)).Label("fault fires exactly when the write is longer than the allowance")
                .And(oldOrNew.Label("old or new content, never partial"))
                .And((leftovers == 0).Label("temp file removed"));
        });

    // ---- apply -> undo through the real writer with a fault somewhere in the sequence ----

    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));

    [Property(MaxTest = 200)]
    public Property ApplyThenUndo_WithAFaultAnywhere_EndsByteIdenticalToTheStart() =>
        Prop.ForAll(
            (from realmlist in OptionalBytesGen
             from config in OptionalBytesGen
             from failOnWrite in Gen.Choose(1, 9)
             from allowed in Gen.Choose(0, 30)
             select (realmlist, config, failOnWrite, allowed)).ToArbitrary(),
            input =>
            {
                var (realmlist, config, failOnWrite, allowed) = input;
                using var dir = new TempDirectory();
                var client = dir.Combine("client");
                Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
                Put(client, "Data/enUS/realmlist.wtf", realmlist);
                Put(client, "WTF/Config.wtf", config);
                var before = Snapshot(client);

                var applier = new ClientFilesApplier(
                    new ClientFilesPlanner(), new WriteFault(failOnWrite, allowed).Writer(), new NeverRunning(), dir.Combine("backups"));
                var plan = applier.Plan(new ClientFilesRequest("inst", "prof", client, V3, "set realmlist logon.example.org\r\n", "Realm"));

                var applied = true;
                try
                {
                    applier.Apply(plan);
                }
                catch (ClientFilesException)
                {
                    applied = false;
                }

                // A failed apply must already have put everything back.
                var afterFailedApply = applied || Snapshot(client).SequenceEqual(before);

                if (applied)
                {
                    try
                    {
                        applier.Undo("inst");
                    }
                    catch (ClientFilesException)
                    {
                        applier.Undo("inst"); // the fault fired in undo: running it again finishes the job
                    }
                }

                return afterFailedApply.Label("failed apply reverted").And(Snapshot(client).SequenceEqual(before).Label("byte identical at the end"));
            });

    // ---- switch -> undo of the active server with a fault in a write ----

    [Property(MaxTest = 150)]
    public Property SwitchThenUndo_WithAFailingWriter_NeverCorruptsFiles_AndRecoversOnRetry() =>
        Prop.ForAll(Gen.Choose(1, 3).ToArbitrary(), Gen.Choose(0, 400).ToArbitrary(), Gen.Elements(true, false).ToArbitrary(), (failOnWrite, allowed, faultInSwitch) =>
        {
            using var dir = new TempDirectory();
            var config = dir.Combine("config");
            var projection = dir.Combine("wow-servidores");
            var store = new ServerStore(config, new AtomicFileWriter());
            var a = store.Add(Server.Create("Alpha", "alpha.example.org", "R1"));
            var b = store.Add(Server.Create("Beta", "beta.example.org", null));
            var healthy = new ActiveServerManager(store, config, projection, new AtomicFileWriter());
            healthy.Switch(a);
            var activo = ActiveProjection.FilePath(projection);
            var statePath = Path.Combine(config, ActiveServerManager.StateFileName);
            var projA = ActiveProjection.Serialize(a);
            var projB = ActiveProjection.Serialize(b);

            var faulty = new ActiveServerManager(store, config, projection, new WriteFault(failOnWrite, allowed).Writer());
            if (faultInSwitch)
            {
                Try(() => faulty.Switch(b));
            }
            else
            {
                healthy.Switch(b);
                Try(() => faulty.Undo());
            }

            var activoNow = File.ReadAllBytes(activo);
            var wholeFiles = (activoNow.SequenceEqual(projA) || activoNow.SequenceEqual(projB)) && IsJson(statePath);

            // Retry with a healthy writer: the switch completes and one undo goes back to Alpha.
            healthy.Switch(b);
            var restored = healthy.Undo();
            return wholeFiles.Label("activo.json and state.json are whole files")
                .And((restored == a).Label("undo restores the previous server"))
                .And(File.ReadAllBytes(activo).SequenceEqual(projA).Label("activo.json is Alpha again"))
                .And((healthy.GetActive() == a).Label("state agrees"));
        });

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (IOException)
        {
        }
        catch (StoreException)
        {
        }
    }

    private static bool IsJson(string path)
    {
        try
        {
            using var _ = JsonDocument.Parse(File.ReadAllBytes(path));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void Put(string root, string relative, byte[]? bytes)
    {
        if (bytes is null)
            return;
        var path = Path.Combine([root, .. relative.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(root, p), p => Convert.ToHexString(File.ReadAllBytes(p)));

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
