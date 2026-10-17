using System.Text.Json.Nodes;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
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
        Edit(env.ManifestPath, node => node["files"] = new JsonArray());

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal(switched, File.ReadAllText(env.RealmPath));
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
        return new Env(temp, client, realmPath, configPath, applier, Path.Combine(backups, "install-1", result.BackupId!, "manifest.json"));
    }

    private static void Edit(string manifestPath, Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        edit(node);
        File.WriteAllText(manifestPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Env(TempDirectory temp, string client, string realmPath, string configPath, ClientFilesApplier applier, string manifestPath) : IDisposable
    {
        public string Client { get; } = client;
        public string RealmPath { get; } = realmPath;
        public string ConfigPath { get; } = configPath;
        public ClientFilesApplier Applier { get; } = applier;
        public string ManifestPath { get; } = manifestPath;
        public void Dispose() => temp.Dispose();
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
