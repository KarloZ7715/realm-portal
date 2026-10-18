using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class Finding65ManifestIntegrityTests
{
    private const string BlobA = "set realmlist logon-a.example.org\r\n";
    private const string OldRealm = "set realmlist old.example.org\r\n";
    private const string OldConfig = "SET realmList \"old.example.org\"\r\n";
    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));

    [Fact]
    public void Undo_EmptyFileList_IsVerifyFailed()
    {
        using var env = Apply(withConfig: false);
        var switched = File.ReadAllText(env.RealmPath);
        var config = File.ReadAllBytes(env.ConfigPath);
        Edit(env.ManifestPath, node => node["files"] = new JsonArray());

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal(switched, File.ReadAllText(env.RealmPath));
        Assert.Equal(config, File.ReadAllBytes(env.ConfigPath));
    }

    [Fact]
    public void Undo_RemovedEntryForFileThatExisted_IsVerifyFailed_AndLeavesRealmlist()
    {
        using var env = Apply(withConfig: true);
        var switchedRealm = File.ReadAllText(env.RealmPath);
        var switchedConfig = File.ReadAllText(env.ConfigPath);
        Edit(env.ManifestPath, node =>
        {
            var files = node["files"]!.AsArray();
            for (var i = files.Count - 1; i >= 0; i--)
            {
                if (files[i]!["path"]!.GetValue<string>() == "WTF/Config.wtf")
                    files.RemoveAt(i);
            }
        });

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal(switchedRealm, File.ReadAllText(env.RealmPath));
        Assert.Equal(switchedConfig, File.ReadAllText(env.ConfigPath));
    }

    [Fact]
    public void Undo_RemovedEntryForFileTheSwitchCreated_RestoresRealmlist_AndLeavesConfig()
    {
        using var env = Apply(withConfig: false);
        var createdConfig = File.ReadAllBytes(env.ConfigPath);
        Edit(env.ManifestPath, node =>
        {
            var files = node["files"]!.AsArray();
            for (var i = files.Count - 1; i >= 0; i--)
            {
                if (files[i]!["path"]!.GetValue<string>() == "WTF/Config.wtf")
                    files.RemoveAt(i);
            }
        });

        var undo = env.Applier.Undo("install-1", env.Client);

        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.DoesNotContain(undo.Files, f => f.RelativePath == "WTF/Config.wtf");
        Assert.Equal(OldRealm, File.ReadAllText(env.RealmPath));
        Assert.Equal(createdConfig, File.ReadAllBytes(env.ConfigPath));
    }

    [Fact]
    public void Undo_ExistedFlippedToFalse_IsVerifyFailed_AndDoesNotDeleteRealmlist()
    {
        using var env = Apply(withConfig: false);
        var switched = File.ReadAllText(env.RealmPath);
        Edit(env.ManifestPath, node =>
        {
            foreach (var file in node["files"]!.AsArray())
            {
                if (file!["path"]!.GetValue<string>() != "Data/enUS/realmlist.wtf")
                    continue;
                var obj = file.AsObject();
                obj["existed"] = false;
                obj.Remove("backup_file");
                obj.Remove("sha256_before");
                obj.Remove("symlink_sha256");
            }
        });

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.True(File.Exists(env.RealmPath));
        Assert.Equal(switched, File.ReadAllText(env.RealmPath));
    }

    [Fact]
    public void Undo_WithExpectedManifestSha256_RejectsFlippedExistedAndDeletedPayload()
    {
        using var env = Apply(withConfig: false);
        var switched = File.ReadAllText(env.RealmPath);
        Assert.Equal(ClientFilesIo.Sha256Hex(File.ReadAllBytes(env.ManifestPath)), env.ManifestSha256);

        FlipRealmlistExistedAndDeletePayload(env.ManifestPath);

        var ex = Assert.Throws<ClientFilesException>(() =>
            env.Applier.Undo("install-1", env.Client, expectedManifestSha256: env.ManifestSha256));

        Assert.Equal(ClientFilesErrorCode.HashMismatch, ex.Code);
        Assert.True(File.Exists(env.RealmPath));
        Assert.Equal(switched, File.ReadAllText(env.RealmPath));
    }

    [Fact]
    public void Undo_TamperedManifestAndDeletedPayload_IsHashMismatch_AndLeavesRealmlist()
    {
        using var env = new PortalEnv();
        Assert.True(env.Service.SwitchServer(new SwitchRequest(env.ClientId, env.Server.Id)).Succeeded);
        var switched = File.ReadAllText(env.RealmPath);
        var journalPath = Path.Combine(env.Config, "last-switch.json");
        var journal = JsonNode.Parse(File.ReadAllText(journalPath))!;
        var backupId = journal["backup_id"]!.GetValue<string>();
        var manifestPath = Path.Combine(env.Backups, env.ClientId, backupId, "manifest.json");
        Assert.Equal(ClientFilesIo.Sha256Hex(File.ReadAllBytes(manifestPath)), journal["manifest_sha256"]!.GetValue<string>());

        FlipRealmlistExistedAndDeletePayload(manifestPath);

        var undo = env.Service.Undo();

        Assert.False(undo.Succeeded);
        Assert.Equal(ServiceErrorCode.HashMismatch, undo.Error!.Code);
        Assert.True(File.Exists(env.RealmPath));
        Assert.Equal(switched, File.ReadAllText(env.RealmPath));
        Assert.Equal(env.Server.Id, env.Service.GetStatus().Value!.Active!.Id);
    }

    [Fact]
    public void Undo_JournalWithoutManifestSha256_StillRestores()
    {
        using var env = new PortalEnv();
        Assert.True(env.Service.SwitchServer(new SwitchRequest(env.ClientId, env.Server.Id)).Succeeded);
        var journalPath = Path.Combine(env.Config, "last-switch.json");
        var journal = JsonNode.Parse(File.ReadAllText(journalPath))!.AsObject();
        journal.Remove("manifest_sha256");
        File.WriteAllText(journalPath, journal.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");

        var undo = env.Service.Undo();

        Assert.True(undo.Succeeded, undo.Error?.Diagnostic);
        Assert.Equal("set realmlist old.example.org\r\n", File.ReadAllText(env.RealmPath));
    }

    private static void FlipRealmlistExistedAndDeletePayload(string manifestPath)
    {
        var node = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        string? backupFile = null;
        foreach (var file in node["files"]!.AsArray())
        {
            if (file!["path"]!.GetValue<string>() != "Data/enUS/realmlist.wtf")
                continue;
            backupFile = file["backup_file"]?.GetValue<string>();
            var obj = file.AsObject();
            obj["existed"] = false;
            obj.Remove("backup_file");
            obj.Remove("sha256_before");
            obj.Remove("symlink_sha256");
        }

        File.WriteAllText(manifestPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        if (backupFile is null)
            return;

        var payload = Path.Combine(Path.GetDirectoryName(manifestPath)!, backupFile.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(payload))
            File.Delete(payload);
        var sidecar = payload + ".symlink";
        if (File.Exists(sidecar))
            File.Delete(sidecar);
    }

    private static Env Apply(bool withConfig)
    {
        var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var realmPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        var configPath = Path.Combine(client, "WTF", "Config.wtf");
        File.WriteAllText(realmPath, OldRealm);
        if (withConfig)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, OldConfig);
        }

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var result = applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        return new Env(
            temp,
            client,
            realmPath,
            configPath,
            applier,
            Path.Combine(backups, "install-1", result.BackupId!, "manifest.json"),
            result.ManifestSha256);
    }

    private static void Edit(string manifestPath, Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        edit(node);
        File.WriteAllText(manifestPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Env(
        TempDirectory temp,
        string client,
        string realmPath,
        string configPath,
        ClientFilesApplier applier,
        string manifestPath,
        string? manifestSha256) : IDisposable
    {
        public string Client { get; } = client;
        public string RealmPath { get; } = realmPath;
        public string ConfigPath { get; } = configPath;
        public ClientFilesApplier Applier { get; } = applier;
        public string ManifestPath { get; } = manifestPath;
        public string? ManifestSha256 { get; } = manifestSha256;
        public void Dispose() => temp.Dispose();
    }

    private sealed class PortalEnv : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public PortalEnv()
        {
            Config = _temp.Combine("config");
            var projection = _temp.Combine("projection");
            Client = _temp.Combine("client");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(projection);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            RealmPath = Path.Combine(Client, "Data", "enUS", "realmlist.wtf");
            File.WriteAllBytes(RealmPath, Encoding.Latin1.GetBytes("set realmlist old.example.org\r\n"));
            Service = new PortalService(Config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => Backups);
            ClientId = Service.AddClient(Client).Value!.Id;
            Server = Service.AddServer(new NewServer("A", "a.example.org")).Value!;
        }

        public string Config { get; }
        public string Client { get; }
        public string Backups { get; }
        public string RealmPath { get; }
        public string ClientId { get; }
        public ServerEntry Server { get; }
        public PortalService Service { get; }
        public void Dispose() => _temp.Dispose();
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec)
        {
        }
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
