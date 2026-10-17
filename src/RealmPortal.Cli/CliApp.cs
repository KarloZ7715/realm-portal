using RealmPortal.Core;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;

namespace RealmPortal.Cli;

/// <summary>
/// The server-management commands of the CLI: add, remove, list, switch, undo, status, launch.
/// Exit codes: 0 success, 1 usage error, 2 operation failed.
/// </summary>
public sealed class CliApp(
    TextWriter output,
    TextWriter error,
    IClientVersionReader versionReader,
    IGameLauncher launcher,
    Func<string, string?> getVariable)
{
    public const string ClientDirVariable = "REALM_PORTAL_CLIENT_DIR";

    private static readonly HashSet<string> ValueOptions =
        ["--config-dir", "--projection-dir", "--client-dir", "--realm", "--line", "--build", "--account", "--wrapper"];

    private static readonly HashSet<string> FlagOptions = ["--all", "--force", "--dry-run", "--verbose"];

    private bool _verbose;

    public static readonly string[] Commands = ["add", "remove", "list", "switch", "undo", "status", "launch"];

    public const string Usage = """
        Usage:
          realm-portal --version
          realm-portal client-version <client-directory>
          realm-portal add <name> <host> [--realm <realm>] [--line <realmlist line>]... [--build <n>] [--account <name>]
          realm-portal remove <server>
          realm-portal list [--all]
          realm-portal switch <server> [--force]
          realm-portal undo
          realm-portal status
          realm-portal launch [<server>] [--force] [--dry-run] [--wrapper <command>]

        Global options (before or after the command):
          --verbose               print detailed error messages (paths, parse errors)

        Common options (they override the environment variables):
          --config-dir <dir>      app config (env REALM_PORTAL_CONFIG_DIR)
          --projection-dir <dir>  where activo.json is written (env REALM_PORTAL_PROJECTION_DIR)
          --client-dir <dir>      game client folder (env REALM_PORTAL_CLIENT_DIR); enables the compatibility filter
        <server> is an id, a unique id prefix or a name (case-insensitive).
        """;

    private sealed class Parsed
    {
        public Dictionary<string, List<string>> Options { get; } = [];
        public HashSet<string> Flags { get; } = [];
        public List<string> Positionals { get; } = [];

        public string? Get(string name) => Options.TryGetValue(name, out var values) ? values[^1] : null;
    }

    private sealed class UsageException(string message) : Exception(message);

    public bool IsCommand(string name) => Commands.Contains(name);

    public int Run(string command, string[] args, bool globalVerbose = false)
    {
        try
        {
            var parsed = Parse(args);
            _verbose = globalVerbose || parsed.Flags.Contains("--verbose");
            var configDir = parsed.Get("--config-dir") ?? PortalPaths.ConfigDirectory(getVariable);
            var projectionDir = parsed.Get("--projection-dir") ?? PortalPaths.ProjectionDirectory(getVariable);
            var clientDir = parsed.Get("--client-dir") ?? Blank(getVariable(ClientDirVariable));
            var service = new PortalService(configDir, projectionDir, versionReader, new GameLauncherAdapter(launcher), getVariable: getVariable);
            string? clientId = null;
            if (clientDir is not null)
            {
                var reg = service.AddClient(clientDir);
                if (!reg.Succeeded)
                    return Fail(reg.Error!);
                clientId = reg.Value!.Id;
            }

            return command switch
            {
                "add" => Add(parsed, service),
                "remove" => Remove(parsed, service),
                "list" => List(parsed, service, clientId, clientDir),
                "switch" => SwitchCommand(parsed, service, clientId, clientDir),
                "undo" => Undo(parsed, service),
                "status" => Status(parsed, service, clientId, clientDir),
                "launch" => Launch(parsed, service, clientId, clientDir, parsed.Flags.Contains("--dry-run")),
                _ => throw new UsageException($"unknown command '{command}'"),
            };
        }
        catch (UsageException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            error.WriteLine(Usage);
            return 1;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private int Fail(ServiceError serviceError)
    {
        error.WriteLine($"error: {ServiceCli.Describe(serviceError, _verbose)}");
        return 2;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Parsed Parse(string[] args)
    {
        var parsed = new Parsed();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (ValueOptions.Contains(arg))
            {
                if (i + 1 >= args.Length)
                    throw new UsageException($"option {arg} needs a value");
                if (args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new UsageException($"option {arg} needs a value");
                if (!parsed.Options.TryGetValue(arg, out var values))
                    parsed.Options[arg] = values = [];
                values.Add(args[++i]);
            }
            else if (FlagOptions.Contains(arg))
            {
                parsed.Flags.Add(arg);
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"unknown option {arg}");
            }
            else
            {
                parsed.Positionals.Add(arg);
            }
        }

        return parsed;
    }

    private static readonly HashSet<string> GlobalCommandFlags = ["--verbose"];

    private static void Expect(Parsed parsed, int min, int max, string what, params string[] allowedFlags)
    {
        if (parsed.Positionals.Count < min || parsed.Positionals.Count > max)
            throw new UsageException($"wrong number of arguments (expected {what})");
        var extra = parsed.Flags.Except(allowedFlags).Except(GlobalCommandFlags).FirstOrDefault();
        if (extra is not null)
            throw new UsageException($"option {extra} does not apply to this command");
    }

    private int Add(Parsed parsed, PortalService service)
    {
        Expect(parsed, 2, 2, "<name> <host>");
        int? build = null;
        if (parsed.Get("--build") is { } text)
        {
            if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0)
                throw new UsageException($"--build must be a positive integer, got '{text}'");
            build = n;
        }

        var lines = parsed.Options.GetValueOrDefault("--line");

        var result = service.AddServer(new NewServer(
            parsed.Positionals[0],
            parsed.Positionals[1],
            parsed.Get("--realm"),
            null,
            build,
            parsed.Get("--account"),
            ExtraRealmlistLines: lines));
        if (!result.Succeeded)
            return Fail(result.Error!);
        output.WriteLine($"Added '{result.Value!.Name}' ({result.Value.Id})");
        return 0;
    }

    private int Remove(Parsed parsed, PortalService service)
    {
        Expect(parsed, 1, 1, "<server>");
        if (FindServer(service, parsed.Positionals[0], out var server) != 0)
            return 2;
        var result = service.RemoveServer(server.Id);
        if (!result.Succeeded)
            return Fail(result.Error!);
        output.WriteLine($"Removed '{server.Name}'");
        return 0;
    }

    private int List(Parsed parsed, PortalService service, string? clientId, string? clientDir)
    {
        Expect(parsed, 0, 0, "no arguments", "--all");
        if (clientDir is not null && clientId is null)
            return 2;

        var showAll = parsed.Flags.Contains("--all");
        var result = service.ListServers(clientId, compatibleOnly: !showAll && clientId is not null);
        if (!result.Succeeded)
            return Fail(result.Error!);

        var rows = result.Value!;
        if (rows.Count == 0)
        {
            var all = service.ListServers();
            output.WriteLine(all.Succeeded && all.Value!.Count == 0 ? "No servers." : "No compatible servers.");
            return 0;
        }

        foreach (var server in rows)
        {
            var build = server.RequiredClientBuild?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "any";
            output.WriteLine(
                $"{(server.IsActive ? "*" : " ")} {server.Id}  {server.Name}  {server.Host}  build={build}"
                + (server.IsCompatible ? "" : "  [incompatible]"));
        }

        return 0;
    }

    private int SwitchCommand(Parsed parsed, PortalService service, string? clientId, string? clientDir)
    {
        Expect(parsed, 1, 1, "<server>", "--force");
        if (FindServer(service, parsed.Positionals[0], out var server) != 0)
            return 2;
        var force = parsed.Flags.Contains("--force");

        if (clientId is null)
        {
            if (server.RequiredClientBuild is not null)
                error.WriteLine($"warning: no --client-dir given; compatibility with build {server.RequiredClientBuild} was not checked.");
            var status = service.GetStatus();
            var previousName = status.Succeeded ? status.Value!.Active?.Name : null;
            var activated = service.ActivateWithoutClientFiles(server.Id);
            if (!activated.Succeeded)
                return Fail(activated.Error!);
            output.WriteLine($"Switched to '{server.Name}'" + (previousName is null || previousName == server.Name ? "" : $" (previous: '{previousName}')"));
            return 0;
        }

        var sw = service.SwitchServer(new SwitchRequest(clientId, server.Id, force));
        if (!sw.Succeeded)
            return Fail(sw.Error!);
        var previous = sw.Value!.Previous?.Name;
        output.WriteLine($"Switched to '{server.Name}'" + (previous is null || previous == server.Name ? "" : $" (previous: '{previous}')"));
        return 0;
    }

    private int Undo(Parsed parsed, PortalService service)
    {
        Expect(parsed, 0, 0, "no arguments");
        var result = service.Undo();
        if (!result.Succeeded)
            return Fail(result.Error!);
        var restored = result.Value!.Active;
        output.WriteLine(restored is null ? "Undone: no active server." : $"Undone: restored '{restored.Name}'.");
        if (result.Value!.ClientFilesNotice is { } notice)
            output.WriteLine(notice);
        return 0;
    }

    private int Status(Parsed parsed, PortalService service, string? clientId, string? clientDir)
    {
        Expect(parsed, 0, 0, "no arguments");
        var result = service.GetStatus();
        if (!result.Succeeded)
            return Fail(result.Error!);
        var status = result.Value!;
        if (status.Active is null)
        {
            output.WriteLine("Active server: none");
        }
        else
        {
            var s = status.Active;
            output.WriteLine($"Active server: {s.Name} ({s.Id})");
            output.WriteLine($"Host: {s.Host}");
            output.WriteLine($"Realm: {(s.Realm.Length == 0 ? "-" : s.Realm)}");
            output.WriteLine($"Required client build: {s.RequiredClientBuild?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "any"}");
        }

        output.WriteLine($"activo.json: {(status.ProjectionInSync ? "in sync" : "OUT OF SYNC (run switch to repair)")}");
        if (status.ClientsNotMatchingActive.Count > 0 && status.Active is not null)
        {
            foreach (var client in status.ClientsNotMatchingActive)
                output.WriteLine(
                    $"Client folder out of sync: {client.Path} (run 'switch {status.Active.Name} --client-dir <folder>')");
        }

        output.WriteLine($"Can undo: {(status.CanUndo ? "yes" : "no")}");
        if (clientDir is not null)
        {
            try
            {
                var client = versionReader.Read(clientDir);
                var compat = status.Active is null ? "" : status.Active.IsCompatible ? " (compatible)" : " (INCOMPATIBLE)";
                output.WriteLine($"Client: {client}{compat}");
            }
            catch (ClientVersionException ex)
            {
                error.WriteLine($"error: {ex.Message}");
                return 2;
            }
        }

        return 0;
    }

    private int Launch(Parsed parsed, PortalService service, string? clientId, string? clientDir, bool dryRun)
    {
        Expect(parsed, 0, 1, "[<server>]", "--force", "--dry-run");
        if (clientDir is null || clientId is null)
            throw new UsageException("launch needs --client-dir (or REALM_PORTAL_CLIENT_DIR)");

        var force = parsed.Flags.Contains("--force");
        var wrapper = parsed.Get("--wrapper");
        if (wrapper is not null && !dryRun)
        {
            var settings = service.GetLauncherSettings();
            if (!settings.Succeeded)
                return Fail(settings.Error!);
            var merged = settings.Value! with { Prefix = [wrapper] };
            var saved = service.SetLauncherSettings(merged);
            if (!saved.Succeeded)
                return Fail(saved.Error!);
        }

        ServerEntry server;
        if (parsed.Positionals.Count == 1)
        {
            if (FindServer(service, parsed.Positionals[0], out server) != 0)
                return 2;
            if (dryRun)
            {
                var executable = GameExecutable.Find(clientDir)
                                 ?? throw new UsageException($"{GameExecutable.FileName} not found in '{clientDir}'.");
                output.WriteLine($"Dry run: would launch {(wrapper is null ? "" : wrapper + " ")}{executable} for '{server.Name}'.");
                return 0;
            }

            var play = service.Play(new SwitchRequest(clientId, server.Id, force));
            if (!play.Succeeded)
                return Fail(play.Error!);
            output.WriteLine($"Launched {GameExecutable.Find(clientDir)} for '{server.Name}'.");
            return 0;
        }

        var status = service.GetStatus();
        if (!status.Succeeded)
            return Fail(status.Error!);
        server = status.Value!.Active ?? throw new InvalidOperationException("no active server; run 'switch <server>' or 'launch <server>'.");
        if (!server.IsCompatible && !force)
        {
            error.WriteLine($"error: '{server.Name}' requires client build {server.RequiredClientBuild}, but the client is incompatible (use --force to launch anyway).");
            return 2;
        }

        if (dryRun)
        {
            var executable = GameExecutable.Find(clientDir)
                             ?? throw new UsageException($"{GameExecutable.FileName} not found in '{clientDir}'.");
            output.WriteLine($"Dry run: would launch {(wrapper is null ? "" : wrapper + " ")}{executable} for '{server.Name}'.");
            return 0;
        }

        var launch = service.Play(new SwitchRequest(clientId, server.Id, force));
        if (!launch.Succeeded)
            return Fail(launch.Error!);
        output.WriteLine($"Launched {GameExecutable.Find(clientDir)} for '{server.Name}'.");
        return 0;
    }

    /// <summary>Resolves a server selector. Returns 0 on success, or 2 after writing an error.</summary>
    private int FindServer(PortalService service, string selector, out ServerEntry server)
    {
        server = null!;
        var list = service.ListServers();
        if (!list.Succeeded)
            return Fail(list.Error!);

        var found = ServiceCli.FindServerEntry(list.Value!, selector);
        if (found is null)
        {
            error.WriteLine($"error: no server matches '{selector}'.");
            return 2;
        }

        server = found;
        return 0;
    }
}
