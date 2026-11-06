using RealmPortal.Cli;
using RealmPortal.Core;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// #194: every CLI empty state and error names the cause and, where one exists, the next step (a command to run),
/// and keeps its exit code. Synthetic clients in temp folders; config and projection folders come from
/// REALM_PORTAL_CONFIG_DIR / REALM_PORTAL_PROJECTION_DIR.
/// </summary>
public class ClearErrorMessagesTests
{
    private sealed class NoLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) =>
            throw new InvalidOperationException("tests never launch the game");
    }

    private sealed class AlwaysRunningGameProbe : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => true;
    }

    private sealed class Box : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public string Config => _dir.Combine("config");
        public string Projection => _dir.Combine("projection");
        public string Root => _dir.Path;

        public string Client(string name, int build = 12340)
        {
            var path = _dir.Combine(name);
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, build)));
            return path;
        }

        private Func<string, string?> Variables()
        {
            var config = Config;
            var projection = Projection;
            return TestBackupDir.Variables(_dir, name => name switch
            {
                PortalPaths.ConfigDirVariable => config,
                PortalPaths.ProjectionDirVariable => projection,
                _ => null,
            });
        }

        public (int Code, string Out, string Err) Run(params string[] args) => RunWith(new NeverRunningGameProbe(), args);

        public (int Code, string Out, string Err) RunWith(IGameProcessProbe probe, params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var code = Program.Run(args, output, error, new PeClientVersionReader(), new NoLauncher(), Variables(), probe);
            return (code, output.ToString(), error.ToString());
        }

        public (int Code, string Out, string Err) Scan()
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var app = new CliApp(output, error, new PeClientVersionReader(), new NoLauncher(), Variables(),
                new ClientScanOptions { IncludeDefaultRoots = false, IncludeSystemRoots = false }, new NeverRunningGameProbe());
            var code = app.Run("scan", ["--root", _dir.Combine("games")]);
            return (code, output.ToString(), error.ToString());
        }

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void List_WithNoServers_SaysHowToAddOrImportOne()
    {
        using var box = new Box();
        var result = box.Run("list");
        Assert.Equal(0, result.Code);
        Assert.Contains("No servers.", result.Out, StringComparison.Ordinal);
        Assert.Contains("realm-portal add <name> <host>", result.Out, StringComparison.Ordinal);
        Assert.Contains("realm-portal scan", result.Out, StringComparison.Ordinal);
        Assert.False(Directory.Exists(box.Projection) && File.Exists(Path.Combine(box.Projection, "activo.json")));
    }

    [Fact]
    public void List_WithOnlyIncompatibleServers_PointsAtListAll()
    {
        using var box = new Box();
        var client = box.Client("wow-335", 12340);
        Assert.Equal(0, box.Run("add", "Classic", "classic.example.test", "--build", "5875").Code);

        var result = box.Run("list", "--client-dir", client);

        Assert.Equal(0, result.Code);
        Assert.Contains("No compatible servers for this client's build.", result.Out, StringComparison.Ordinal);
        Assert.Contains("realm-portal list --all", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientList_WithNoClients_SaysHowToScanOrAddOne()
    {
        using var box = new Box();
        var result = box.Run("client", "list");
        Assert.Equal(0, result.Code);
        Assert.Contains("No clients.", result.Out, StringComparison.Ordinal);
        Assert.Contains("realm-portal scan", result.Out, StringComparison.Ordinal);
        Assert.Contains("realm-portal client add <path>", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_FindingNothing_SaysSoAndOffersAnotherRootOrManualAdd()
    {
        using var box = new Box();
        Directory.CreateDirectory(Path.Combine(box.Root, "games"));

        var result = box.Scan();

        Assert.Equal(0, result.Code);
        Assert.Contains("Scan complete: 0 clients and 0 servers added.", result.Out, StringComparison.Ordinal);
        Assert.Contains("No game clients were found.", result.Out, StringComparison.Ordinal);
        Assert.Contains("realm-portal scan --root <folder>", result.Out, StringComparison.Ordinal);
        Assert.Contains("realm-portal client add <path>", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_FindingOnlyKnownClients_SaysTheyWereKept()
    {
        using var box = new Box();
        var client = box.Client(Path.Combine("games", "wow"));
        Assert.Equal(0, box.Run("client", "add", client).Code);

        var result = box.Scan();

        Assert.Equal(0, result.Code);
        Assert.Contains("No new clients were found; the clients already registered were kept.", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigBackups_WithNoBak_ExplainsWhenOneIsKept()
    {
        using var box = new Box();
        var result = box.Run("config", "backups");
        Assert.Equal(0, result.Code);
        Assert.Contains("No backups yet: a .bak copy is kept the next time a save changes servers.json or clients.json.", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigBackups_DamagedFileWithValidBak_NamesTheRestoreCommand()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "First", "first.example.test").Code);
        Assert.Equal(0, box.Run("add", "Second", "second.example.test").Code);
        File.WriteAllText(Path.Combine(box.Config, "servers.json"), "{ not json");

        var result = box.Run("config", "backups");

        Assert.Equal(0, result.Code);
        Assert.Contains("servers.json is damaged but its backup is valid; run 'realm-portal config restore servers' to restore it.", result.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("No backups yet", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Undo_WithNothingToUndo_PointsAtStatusAndSwitch()
    {
        using var box = new Box();
        var result = box.Run("undo");
        Assert.Equal(2, result.Code);
        Assert.Contains("error: Nothing to undo: there is no earlier switch to go back to.", result.Err, StringComparison.Ordinal);
        Assert.Contains("realm-portal switch <server>", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Undo_ForUnregisteredClientFolder_NamesTheFolderAndHowToFindClients()
    {
        using var box = new Box();
        var folder = box.Client("not-registered");

        var result = box.Run("undo", "--client-dir", folder);

        Assert.Equal(2, result.Code);
        Assert.Contains($"No registered client uses the folder '{folder}'.", result.Err, StringComparison.Ordinal);
        Assert.Contains("realm-portal client list", result.Err, StringComparison.Ordinal);
        Assert.Contains("realm-portal client add <path>", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownServer_PointsAtList()
    {
        using var box = new Box();
        var result = box.Run("switch", "nope");
        Assert.Equal(2, result.Code);
        Assert.Contains("error: no server matches 'nope'. Run 'realm-portal list' to see the server names and ids.", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownClient_PointsAtClientList()
    {
        using var box = new Box();
        var result = box.Run("client", "remove", "nope");
        Assert.Equal(2, result.Code);
        Assert.Contains("error: no client matches 'nope'. Run 'realm-portal client list' to see the registered clients.", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Switch_WhileGameRuns_SaysToCloseTheGame()
    {
        using var box = new Box();
        var client = box.Client("wow");
        Assert.Equal(0, box.Run("add", "A", "a.example.test").Code);

        var result = box.RunWith(new AlwaysRunningGameProbe(), "switch", "A", "--client-dir", client);

        Assert.Equal(2, result.Code);
        Assert.Contains("The game is running", result.Err, StringComparison.Ordinal);
        Assert.Contains("Close the game and run the command again.", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Switch_ToIncompatibleServer_NamesBuildsAndThreeWaysOut()
    {
        using var box = new Box();
        var client = box.Client("wow", 12340);
        Assert.Equal(0, box.Run("add", "Classic", "classic.example.test", "--build", "5875").Code);

        var result = box.Run("switch", "Classic", "--client-dir", client);

        Assert.Equal(2, result.Code);
        Assert.Contains("Server requires client build 5875, but the client is 12340.", result.Err, StringComparison.Ordinal);
        Assert.Contains("realm-portal edit <server> --build <n|any>", result.Err, StringComparison.Ordinal);
        Assert.Contains("--force", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_WithTakenName_SaysToChooseAnother()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", "a.example.test").Code);

        var result = box.Run("add", "a", "b.example.test");

        Assert.Equal(2, result.Code);
        Assert.Contains("A server named", result.Err, StringComparison.Ordinal);
        Assert.Contains("Choose another name", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_WithInvalidHost_NamesTheLineAndAsksToFixIt()
    {
        using var box = new Box();
        var result = box.Run("add", "A", "bad host.example.invalid");
        Assert.Equal(2, result.Code);
        Assert.Contains("error: Invalid realmlist (line 1)", result.Err, StringComparison.Ordinal);
        Assert.Contains("Fix the host or the extra realmlist lines (--line) and run the command again.", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void DamagedStateFile_SaysItWasNotOverwrittenAndHowToRecover()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "A", "a.example.test").Code);
        File.WriteAllText(Path.Combine(box.Config, "state.json"), "{ not json");

        var result = box.Run("status");

        Assert.Equal(2, result.Code);
        Assert.Contains("Config file 'state.json' is corrupt or unreadable.", result.Err, StringComparison.Ordinal);
        Assert.Contains("Realm Portal does not overwrite it", result.Err, StringComparison.Ordinal);
        Assert.Contains("--verbose", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void DamagedServersFileWithValidBak_NamesRestoreOnce_EvenWithVerbose()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("add", "First", "first.example.test").Code);
        Assert.Equal(0, box.Run("add", "Second", "second.example.test").Code);
        File.WriteAllText(Path.Combine(box.Config, "servers.json"), "{ not json");

        var plain = box.Run("list");
        var verbose = box.Run("list", "--verbose");

        foreach (var result in new[] { plain, verbose })
        {
            Assert.Equal(2, result.Code);
            var at = result.Err.IndexOf("realm-portal config restore servers", StringComparison.Ordinal);
            Assert.True(at >= 0, result.Err);
            Assert.Equal(-1, result.Err.IndexOf("realm-portal config restore servers", at + 1, StringComparison.Ordinal));
            Assert.DoesNotContain("fix the file by hand", result.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Status_WithNoActiveServer_SaysHowToChooseOne()
    {
        using var box = new Box();
        var result = box.Run("status");
        Assert.Equal(0, result.Code);
        Assert.Contains("Active server: none (run 'realm-portal switch <server>' to choose one)", result.Out, StringComparison.Ordinal);
    }

    public static TheoryData<ServiceErrorCode, ServiceErrorAction> Actions => new()
    {
        { ServiceErrorCode.GameRunning, ServiceErrorAction.CloseGame },
        { ServiceErrorCode.ProcessCheckFailed, ServiceErrorAction.CloseGame },
        { ServiceErrorCode.PermissionDenied, ServiceErrorAction.CheckPermissions },
        { ServiceErrorCode.ReadOnlyFs, ServiceErrorAction.CheckPermissions },
        { ServiceErrorCode.DiskFull, ServiceErrorAction.FreeDiskSpace },
        { ServiceErrorCode.FileSharingViolation, ServiceErrorAction.TryAgain },
        { ServiceErrorCode.PathNotFound, ServiceErrorAction.ChooseClientFolder },
        { ServiceErrorCode.PathEscapesClient, ServiceErrorAction.ReviewClientFiles },
        { ServiceErrorCode.HashMismatch, ServiceErrorAction.TryAgain },
        { ServiceErrorCode.VerifyFailed, ServiceErrorAction.CheckPermissions },
        { ServiceErrorCode.UnsupportedClient, ServiceErrorAction.ChooseClientFolder },
        { ServiceErrorCode.InvalidRealmlist, ServiceErrorAction.FixInput },
        { ServiceErrorCode.NothingToUndo, ServiceErrorAction.ChooseServer },
        { ServiceErrorCode.UndoConflict, ServiceErrorAction.ResolveUndoConflict },
        { ServiceErrorCode.ClientExecutableNotFound, ServiceErrorAction.ChooseClientFolder },
        { ServiceErrorCode.ClientNotAPortableExecutable, ServiceErrorAction.ChooseClientFolder },
        { ServiceErrorCode.ClientNoVersionResource, ServiceErrorAction.ChooseClientFolder },
        { ServiceErrorCode.ClientUnreadable, ServiceErrorAction.CheckPermissions },
        { ServiceErrorCode.ServerNotFound, ServiceErrorAction.ChooseServer },
        { ServiceErrorCode.ClientNotFound, ServiceErrorAction.ScanOrAddClient },
        { ServiceErrorCode.BackupNotFound, ServiceErrorAction.None },
        { ServiceErrorCode.ClientAlreadyExists, ServiceErrorAction.FixInput },
        { ServiceErrorCode.InvalidClientInput, ServiceErrorAction.FixInput },
        { ServiceErrorCode.ServerNameTaken, ServiceErrorAction.FixInput },
        { ServiceErrorCode.InvalidServerInput, ServiceErrorAction.FixInput },
        { ServiceErrorCode.IncompatibleClient, ServiceErrorAction.UseCompatibleClient },
        { ServiceErrorCode.StoreCorrupt, ServiceErrorAction.None },
        { ServiceErrorCode.StoreLocked, ServiceErrorAction.TryAgain },
        { ServiceErrorCode.UndoTargetMissing, ServiceErrorAction.ChooseServer },
        { ServiceErrorCode.InvalidLauncherSettings, ServiceErrorAction.CheckLauncherSettings },
        { ServiceErrorCode.LaunchFailed, ServiceErrorAction.CheckLauncherSettings },
        { ServiceErrorCode.UpdateCheckFailed, ServiceErrorAction.TryAgain },
        { ServiceErrorCode.ChecksumMismatch, ServiceErrorAction.TryAgain },
        { ServiceErrorCode.AssetMissing, ServiceErrorAction.TryAgain },
        { ServiceErrorCode.DownloadTooLarge, ServiceErrorAction.TryAgain },
        { ServiceErrorCode.UntrustedRedirect, ServiceErrorAction.TryAgain },
        { ServiceErrorCode.RateLimited, ServiceErrorAction.TryAgain },
        { ServiceErrorCode.InconsistentState, ServiceErrorAction.ReviewClientFiles },
        { ServiceErrorCode.Unexpected, ServiceErrorAction.TryAgain },
    };

    [Theory]
    [MemberData(nameof(Actions))]
    public void NextAction_IsStablePerCode(ServiceErrorCode code, ServiceErrorAction expected) =>
        Assert.Equal(expected, new ServiceError(code).NextAction());

    [Fact]
    public void NextAction_TableCoversEveryCode()
    {
        var listed = Actions.Select(row => (ServiceErrorCode)row[0]).ToHashSet();
        Assert.All(Enum.GetValues<ServiceErrorCode>(), code => Assert.Contains(code, listed));
    }

    [Theory]
    [InlineData("/tmp/config/servers.json")]
    [InlineData("/tmp/config/clients.json")]
    public void NextAction_DamagedServersOrClientsFile_IsRestoreConfigBackup(string path) =>
        Assert.Equal(ServiceErrorAction.RestoreConfigBackup, new ServiceError(ServiceErrorCode.StoreCorrupt, Path: path).NextAction());

    [Fact]
    public void EveryCodeWithAnAction_HasACliNextStep()
    {
        foreach (var code in Enum.GetValues<ServiceErrorCode>())
        {
            var error = new ServiceError(code);
            // StoreLocked's cause already ends with "wait for it and try again".
            if (error.NextAction() != ServiceErrorAction.None && code != ServiceErrorCode.StoreLocked)
                Assert.False(string.IsNullOrWhiteSpace(ServiceCli.NextStep(error, "")), code.ToString());
        }
    }

    [Fact]
    public void LaunchFailedAfterApply_NextStepDoesNotRepeatTheUndoLine()
    {
        var message = ServiceCli.Describe(new ServiceError(ServiceErrorCode.LaunchFailed, Consistency: Consistency.AppliedLaunchFailed, Diagnostic: "start failed"));
        Assert.StartsWith("start failed.", message, StringComparison.Ordinal);
        Assert.Contains("realm-portal launch --dry-run", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Run 'undo'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void PermissionDenied_WithPath_NamesItAndDoesNotPointAtVerbose()
    {
        var message = ServiceCli.Describe(new ServiceError(ServiceErrorCode.PermissionDenied, Path: "/tmp/config/servers.json"));
        Assert.Equal(
            "Permission denied: '/tmp/config/servers.json' cannot be read or written. Check that your user owns it and can write to it, or run the command from an account that can, then try again.",
            message);
        Assert.DoesNotContain("--verbose", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sudo", message, StringComparison.Ordinal);
    }

    [Fact]
    public void PermissionDenied_WithoutPath_NamesTheFoldersToCheck()
    {
        var message = ServiceCli.Describe(new ServiceError(ServiceErrorCode.PermissionDenied));
        Assert.Equal(
            "Permission denied: a file or folder Realm Portal needs cannot be read or written. Check that your user owns the config and client folders and can write to them, or run the command from an account that can, then try again.",
            message);
        Assert.DoesNotContain("--verbose", message, StringComparison.Ordinal);
    }

    [Fact]
    public void UndoConflict_ListsTheChangedFiles()
    {
        var message = ServiceCli.Describe(new ServiceError(ServiceErrorCode.UndoConflict, ConflictingFiles: ["Data/enUS/realmlist.wtf"], Consistency: Consistency.Pending));
        Assert.Contains("undo stopped without overwriting them: Data/enUS/realmlist.wtf.", message, StringComparison.Ordinal);
        Assert.Contains("realm-portal undo", message, StringComparison.Ordinal);
    }
}
