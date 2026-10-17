using RealmPortal.Core;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
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

    private static readonly HashSet<string> FlagOptions = ["--all", "--force", "--dry-run"];

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

    public int Run(string command, string[] args)
    {
        try
        {
            var parsed = Parse(args);
            var writer = new AtomicFileWriter();
            var configDir = parsed.Get("--config-dir") ?? PortalPaths.ConfigDirectory(getVariable);
            var projectionDir = parsed.Get("--projection-dir") ?? PortalPaths.ProjectionDirectory(getVariable);
            var clientDir = parsed.Get("--client-dir") ?? Blank(getVariable(ClientDirVariable));
            var store = new ServerStore(configDir, writer);
            var manager = new ActiveServerManager(store, configDir, projectionDir, writer);

            return command switch
            {
                "add" => Add(parsed, store),
                "remove" => Remove(parsed, store, manager),
                "list" => List(parsed, store, manager, clientDir),
                "switch" => SwitchCommand(parsed, store, manager, clientDir),
                "undo" => Undo(parsed, manager),
                "status" => Status(parsed, manager, clientDir),
                "launch" => Launch(parsed, store, manager, clientDir),
                _ => throw new UsageException($"unknown command '{command}'"),
            };
        }
        catch (UsageException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            error.WriteLine(Usage);
            return 1;
        }
        catch (Exception ex) when (ex is StoreException or InvalidRealmlistException or ClientVersionException or ArgumentException or IOException
                                       or UnauthorizedAccessException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            error.WriteLine($"error: {ex.Message}");
            return 2;
        }
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

    private static void Expect(Parsed parsed, int min, int max, string what, params string[] allowedFlags)
    {
        if (parsed.Positionals.Count < min || parsed.Positionals.Count > max)
            throw new UsageException($"wrong number of arguments (expected {what})");
        var extra = parsed.Flags.Except(allowedFlags).FirstOrDefault();
        if (extra is not null)
            throw new UsageException($"option {extra} does not apply to this command");
    }

    private int Add(Parsed parsed, ServerStore store)
    {
        Expect(parsed, 2, 2, "<name> <host>");
        int? build = null;
        if (parsed.Get("--build") is { } text)
        {
            if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0)
                throw new UsageException($"--build must be a positive integer, got '{text}'");
            build = n;
        }

        var server = Server.Create(
            parsed.Positionals[0],
            parsed.Positionals[1],
            parsed.Get("--realm"),
            parsed.Options.GetValueOrDefault("--line"),
            build,
            parsed.Get("--account"));
        store.Add(server);
        output.WriteLine($"Added '{server.Name}' ({server.Id})");
        return 0;
    }

    private int Remove(Parsed parsed, ServerStore store, ActiveServerManager manager)
    {
        Expect(parsed, 1, 1, "<server>");
        var server = FindOrThrow(store, parsed.Positionals[0]);
        manager.Remove(server.Id);
        output.WriteLine($"Removed '{server.Name}'");
        return 0;
    }

    private int List(Parsed parsed, ServerStore store, ActiveServerManager manager, string? clientDir)
    {
        Expect(parsed, 0, 0, "no arguments", "--all");
        var servers = store.List();
        var client = clientDir is null ? null : versionReader.Read(clientDir);
        var activeId = manager.GetActive()?.Id;
        var showAll = parsed.Flags.Contains("--all");

        var rows = servers
            .Select(s => (Server: s, Compatible: clientDir is null || CompatibilityFilter.IsCompatible(s, client)))
            .Where(r => showAll || r.Compatible)
            .ToList();
        if (rows.Count == 0)
        {
            output.WriteLine(servers.Count == 0 ? "No servers." : "No compatible servers.");
            return 0;
        }

        foreach (var (server, compatible) in rows)
        {
            var build = server.RequiredClientBuild?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "any";
            output.WriteLine(
                $"{(server.Id == activeId ? "*" : " ")} {server.Id}  {server.Name}  {server.Host}  build={build}"
                + (compatible ? "" : "  [incompatible]"));
        }

        return 0;
    }

    private int SwitchCommand(Parsed parsed, ServerStore store, ActiveServerManager manager, string? clientDir)
    {
        Expect(parsed, 1, 1, "<server>", "--force");
        var server = FindOrThrow(store, parsed.Positionals[0]);
        SwitchTo(server, manager, clientDir, parsed.Flags.Contains("--force"));
        return 0;
    }

    private void SwitchTo(Server server, ActiveServerManager manager, string? clientDir, bool force)
    {
        if (clientDir is not null)
        {
            var client = versionReader.Read(clientDir);
            if (!CompatibilityFilter.IsCompatible(server, client) && !force)
                throw new InvalidOperationException(
                    $"'{server.Name}' requires client build {server.RequiredClientBuild}, but the client is {client} (use --force to switch anyway).");
        }
        else if (server.RequiredClientBuild is not null)
        {
            error.WriteLine($"warning: no --client-dir given; compatibility with build {server.RequiredClientBuild} was not checked.");
        }

        var previousName = manager.GetActive()?.Name;
        manager.Switch(server);
        output.WriteLine($"Switched to '{server.Name}'" + (previousName is null || previousName == server.Name ? "" : $" (previous: '{previousName}')"));
    }

    private int Undo(Parsed parsed, ActiveServerManager manager)
    {
        Expect(parsed, 0, 0, "no arguments");
        var restored = manager.Undo();
        output.WriteLine(restored is null ? "Undone: no active server." : $"Undone: restored '{restored.Name}'.");
        return 0;
    }

    private int Status(Parsed parsed, ActiveServerManager manager, string? clientDir)
    {
        Expect(parsed, 0, 0, "no arguments");
        var status = manager.Status();
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
        output.WriteLine($"Can undo: {(status.CanUndo ? "yes" : "no")}");
        if (clientDir is not null)
        {
            var client = versionReader.Read(clientDir);
            var compat = status.Active is null ? "" : CompatibilityFilter.IsCompatible(status.Active, client) ? " (compatible)" : " (INCOMPATIBLE)";
            output.WriteLine($"Client: {client}{compat}");
        }

        return 0;
    }

    private int Launch(Parsed parsed, ServerStore store, ActiveServerManager manager, string? clientDir)
    {
        Expect(parsed, 0, 1, "[<server>]", "--force", "--dry-run");
        if (clientDir is null)
            throw new UsageException("launch needs --client-dir (or REALM_PORTAL_CLIENT_DIR)");

        var force = parsed.Flags.Contains("--force");
        Server server;
        if (parsed.Positionals.Count == 1)
        {
            server = FindOrThrow(store, parsed.Positionals[0]);
            SwitchTo(server, manager, clientDir, force);
        }
        else
        {
            server = manager.GetActive() ?? throw new InvalidOperationException("no active server; run 'switch <server>' or 'launch <server>'.");
            var client = versionReader.Read(clientDir);
            if (!CompatibilityFilter.IsCompatible(server, client) && !force)
                throw new InvalidOperationException(
                    $"'{server.Name}' requires client build {server.RequiredClientBuild}, but the client is {client} (use --force to launch anyway).");
        }

        var executable = GameExecutable.Find(clientDir)
                         ?? throw new InvalidOperationException($"{GameExecutable.FileName} not found in '{clientDir}'.");
        var wrapper = parsed.Get("--wrapper");
        if (parsed.Flags.Contains("--dry-run"))
        {
            output.WriteLine($"Dry run: would launch {(wrapper is null ? "" : wrapper + " ")}{executable} for '{server.Name}'.");
            return 0;
        }

        launcher.Launch(executable, clientDir, wrapper);
        output.WriteLine($"Launched {executable} for '{server.Name}'.");
        return 0;
    }

    private static Server FindOrThrow(ServerStore store, string selector) =>
        store.Find(selector) ?? throw new InvalidOperationException($"no server matches '{selector}'.");
}
