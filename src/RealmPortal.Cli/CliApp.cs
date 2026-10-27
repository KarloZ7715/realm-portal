using RealmPortal.Core;
using RealmPortal.Core.ClientFiles;
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
    Func<string, string?> getVariable,
    ClientScanOptions? scanOptions = null,
    IGameProcessProbe? probe = null)
{
    public const string ClientDirVariable = "REALM_PORTAL_CLIENT_DIR";

    private static readonly HashSet<string> ValueOptions =
        ["--config-dir", "--projection-dir", "--client-dir", "--realm", "--line", "--build", "--account", "--wrapper", "--name", "--path", "--host", "--client", "--root"];

    private static readonly HashSet<string> FlagOptions = ["--all", "--force", "--dry-run", "--verbose"];

    private bool _verbose;

    public static readonly string[] Commands = ["add", "remove", "list", "switch", "undo", "status", "launch", "client", "edit", "scan"];

    public const string Usage = """
        Usage:
          realm-portal --version
          realm-portal --help
          realm-portal -h
          realm-portal client-version <client-directory>
          realm-portal add <name> <host> [--realm <realm>] [--line <realmlist line>]... [--build <n>] [--account <name>]
          realm-portal remove <server>
          realm-portal list [--all]
          realm-portal switch <server> [--force]
          realm-portal undo
          realm-portal status
          realm-portal launch [<server>] [--force] [--dry-run] [--wrapper <command>]
          realm-portal client list
          realm-portal client add <path> [--name <name>]
          realm-portal client edit <client> [--name <name>] [--path <path>]
          realm-portal client remove <client>
          realm-portal edit <server> [--name <name>] [--host <host>] [--realm <realm>] [--build <n|any>] [--account <name>] [--client <client|none>]
          realm-portal scan [--root <folder>]...

        Global options (before or after the command):
          --verbose               print detailed error messages (paths, parse errors)

        Common options (before or after the command; they override the environment variables):
          --config-dir <dir>      app config (env REALM_PORTAL_CONFIG_DIR)
          --projection-dir <dir>  where activo.json is written (env REALM_PORTAL_PROJECTION_DIR)
          --client-dir <dir>      game client folder (env REALM_PORTAL_CLIENT_DIR); list/switch/status/launch filter, or scopes undo
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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    public bool IsCommand(string name) => Commands.Contains(name);

    /// <summary>Options that may appear before the subcommand in <see cref="Program.Run"/>.</summary>
    public static bool IsKnownLeadingOption(string arg) =>
        IsLeadingValueOption(arg) || arg is "--verbose";

    /// <summary>The common options that take a value and may also be written before the command.</summary>
    public static bool IsLeadingValueOption(string arg) =>
        arg is "--config-dir" or "--projection-dir" or "--client-dir";

    public int Run(string command, string[] args, bool globalVerbose = false)
    {
        try
        {
            var parsed = Parse(args);
            _verbose = globalVerbose || parsed.Flags.Contains("--verbose");
            var configDir = parsed.Get("--config-dir") ?? PortalPaths.ConfigDirectory(getVariable);
            var projectionDir = parsed.Get("--projection-dir") ?? PortalPaths.ProjectionDirectory(getVariable);
            // Explicit --client-dir only (not REALM_PORTAL_CLIENT_DIR) scopes undo (#149).
            // A supplied but blank --client-dir is a usage error (do not fall back to the env var).
            var rawClientDirOption = parsed.Get("--client-dir");
            if (rawClientDirOption is not null && string.IsNullOrWhiteSpace(rawClientDirOption))
                throw new UsageException("option --client-dir needs a value");
            var explicitClientDir = Blank(rawClientDirOption);
            var clientDir = explicitClientDir ?? Blank(getVariable(ClientDirVariable));
            var service = new PortalService(configDir, projectionDir, versionReader, new GameLauncherAdapter(launcher),
                probe, getVariable: getVariable, scanOptions: scanOptions);
            string? clientId = null;
            if (clientDir is not null && command is "list" or "switch" or "status" or "launch")
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
                "undo" => Undo(parsed, service, explicitClientDir),
                "status" => Status(parsed, service, clientId, clientDir),
                "launch" => Launch(parsed, service, clientId, clientDir, parsed.Flags.Contains("--dry-run")),
                "client" => ClientCommand(parsed, service),
                "edit" => EditServer(parsed, service),
                "scan" => Scan(parsed, service),
                _ => throw new UsageException($"unknown command '{command}'"),
            };
        }
        catch (UsageException ex)
        {
            SafeWriteLine(error, $"error: {ex.Message}");
            error.WriteLine(Usage);
            return 1;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            SafeWriteLine(error, $"error: {ex.Message}");
            return 2;
        }
    }

    private int Fail(ServiceError serviceError)
    {
        SafeWriteLine(error, $"error: {ServiceCli.Describe(serviceError, _verbose)}");
        return 2;
    }

    /// <summary>
    /// Same failure as <see cref="Fail"/>. When launch failed after the switch was applied
    /// (<see cref="ServiceErrorCode.LaunchFailed"/> and <see cref="Consistency.AppliedLaunchFailed"/>)
    /// and <see cref="PortalStatus.CanUndo"/> is true, also say the server is still applied and that
    /// <c>undo</c> can restore the previous one.
    /// </summary>
    private int FailLaunch(ServiceError serviceError, string serverName, PortalService service)
    {
        var code = Fail(serviceError);
        if (serviceError.Code == ServiceErrorCode.LaunchFailed
            && serviceError.Consistency == Consistency.AppliedLaunchFailed)
        {
            var status = service.GetStatus();
            if (status.Succeeded && status.Value!.CanUndo)
            {
                SafeWriteLine(error,
                    $"The game did not start, but '{serverName}' is still applied to this client. Run 'undo' to restore the previous server.");
            }
        }

        return code;
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


    /// <summary>Accepts a pasted <c>set realmlist …</c> line the same way the App add-server form does.</summary>
    private static string NormalizeHostArgument(string raw)
    {
        var pasted = RealmlistAddress.NormalizePastedAddress(raw);

        // Prefer the canonical host[:port] form when the token parses; otherwise pass the (possibly
        // empty) token through so AddServer/UpdateServer report the typed Core error and exit 2.
        if (RealmlistAddress.TryParseHostAddress(pasted.Token, out var host, out var port))
            return port == RealmlistAddress.DefaultPort ? host : $"{host}:{port}";

        return pasted.Token;
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
            NormalizeHostArgument(parsed.Positionals[1]),
            parsed.Get("--realm"),
            null,
            build,
            parsed.Get("--account"),
            ExtraRealmlistLines: lines));
        if (!result.Succeeded)
            return Fail(result.Error!);
        SafeWriteLine(output, $"Added '{result.Value!.Name}' ({result.Value.Id})");
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
        SafeWriteLine(output, $"Removed '{server.Name}'");
        return 0;
    }

    private int EditServer(Parsed parsed, PortalService service)
    {
        Expect(parsed, 1, 1, "<server>");
        if (FindServer(service, parsed.Positionals[0], out var server) != 0)
            return 2;

        int? build = null; // null keeps the stored build
        var clearBuild = false;
        if (parsed.Get("--build") is { } text)
        {
            if (text.Equals("any", StringComparison.OrdinalIgnoreCase))
                clearBuild = true;
            else if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedBuild) || parsedBuild <= 0)
                throw new UsageException($"--build must be a positive integer or 'any', got '{text}'");
            else
                build = parsedBuild;
        }

        var clientId = server.ClientId;
        if (parsed.Get("--client") is { } clientSelector)
        {
            if (clientSelector.Equals("none", StringComparison.OrdinalIgnoreCase))
                clientId = null;
            else if (FindClient(service, clientSelector, out var selected) != 0)
                return 2;
            else
                clientId = selected.Id;
        }

        var extraLines = server.Realmlist.Split(RealmlistText.LineEnding, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
        var host = parsed.Get("--host") is { } hostRaw
            ? NormalizeHostArgument(hostRaw)
            : server.Host;
        var result = service.UpdateServer(new UpdateServerRequest(
            server.Id,
            parsed.Get("--name") ?? server.Name,
            host,
            parsed.Get("--realm") ?? server.Realm,
            RequiredClientBuild: build,
            Account: parsed.Options.ContainsKey("--account") ? parsed.Get("--account") : server.Account,
            ClientId: clientId,
            ExtraRealmlistLines: extraLines,
            ClearRequiredClientBuild: clearBuild));
        if (!result.Succeeded)
            return Fail(result.Error!);
        SafeWriteLine(output, $"Updated '{server.Name}' to '{result.Value!.Name}' ({result.Value.Id})");
        return 0;
    }

    private int Scan(Parsed parsed, PortalService service)
    {
        Expect(parsed, 0, 0, "no arguments");
        var progress = new InlineProgress<ScanProgress>(item =>
            SafeWriteLine(output,
                $"Scanning {UserFacingMessages.Sanitize(item.CurrentSource)} ({item.DirectoriesInspected} directories, {item.ClientsFound} clients, {item.ServersImported} servers imported)"));
        var result = service.Scan(new ScanRequest(AdditionalRoots: parsed.Options.GetValueOrDefault("--root")), progress);
        if (!result.Succeeded)
            return Fail(result.Error!);
        SafeWriteLine(output, $"Scan complete: {result.Value!.AddedClients.Count} clients and {result.Value.ImportedServers.Count} servers added.");
        return 0;
    }

    private int ClientCommand(Parsed parsed, PortalService service)
    {
        if (parsed.Positionals.Count == 0)
            throw new UsageException("client requires list, add, edit, or remove");
        var action = parsed.Positionals[0];
        var selectorArgs = parsed.Positionals.Skip(1).ToList();
        switch (action)
        {
            case "list":
                Expect(parsed, 1, 1, "list");
                var listed = service.ListClients();
                if (!listed.Succeeded)
                    return Fail(listed.Error!);
                if (listed.Value!.Count == 0)
                    SafeWriteLine(output, "No clients.");
                foreach (var entry in listed.Value)
                    SafeWriteLine(output, $"{entry.Id}  {entry.Name}  {entry.Path}  origin={entry.Origin.ToString().ToLowerInvariant()}");
                return 0;
            case "add":
                Expect(parsed, 2, 2, "add <path>");
                var added = service.AddClient(selectorArgs[0], parsed.Get("--name"));
                if (!added.Succeeded)
                    return Fail(added.Error!);
                SafeWriteLine(output, $"Added client '{added.Value!.Name}' ({added.Value.Id})");
                return 0;
            case "edit":
                Expect(parsed, 2, 2, "edit <client>");
                if (FindClient(service, selectorArgs[0], out var existing) != 0)
                    return 2;
                var update = service.UpdateClient(new UpdateClientRequest(
                    existing.Id,
                    parsed.Options.ContainsKey("--name") ? parsed.Get("--name") : existing.Name,
                    parsed.Get("--path")));
                if (!update.Succeeded)
                    return Fail(update.Error!);
                SafeWriteLine(output, $"Updated client '{update.Value!.Name}' ({update.Value.Id})");
                return 0;
            case "remove":
                Expect(parsed, 2, 2, "remove <client>");
                if (FindClient(service, selectorArgs[0], out var client) != 0)
                    return 2;
                var removed = service.RemoveClient(client.Id);
                if (!removed.Succeeded)
                    return Fail(removed.Error!);
                SafeWriteLine(output, $"Removed client '{client.Name}'");
                return 0;
            default:
                throw new UsageException($"unknown client command '{action}'");
        }
    }

    private int FindClient(PortalService service, string selector, out ClientEntry client)
    {
        client = null!;
        var listed = service.ListClients();
        if (!listed.Succeeded)
            return Fail(listed.Error!);
        var clients = listed.Value!;
        client = clients.FirstOrDefault(c => c.Id == selector
            || string.Equals(c.Name, selector, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Path, selector, StringComparison.OrdinalIgnoreCase))!;
        if (client is null && selector.Length > 0)
        {
            var matches = clients.Where(c => c.Id.StartsWith(selector, StringComparison.Ordinal)).Take(2).ToList();
            if (matches.Count == 1)
                client = matches[0];
        }
        if (client is null)
        {
            SafeWriteLine(error, $"error: no client matches '{selector}'.");
            return 2;
        }
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
            SafeWriteLine(output, all.Succeeded && all.Value!.Count == 0 ? "No servers." : "No compatible servers.");
            return 0;
        }

        foreach (var server in rows)
        {
            var build = server.RequiredClientBuild?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "any";
            SafeWriteLine(output,
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
                SafeWriteLine(error, $"warning: no --client-dir given; compatibility with build {server.RequiredClientBuild} was not checked.");
            var status = service.GetStatus();
            var previousName = status.Succeeded ? status.Value!.Active?.Name : null;
            var activated = service.ActivateWithoutClientFiles(server.Id);
            if (!activated.Succeeded)
                return Fail(activated.Error!);
            SafeWriteLine(output, $"Switched to '{server.Name}'" + (previousName is null || previousName == server.Name ? "" : $" (previous: '{previousName}')"));
            return 0;
        }

        var sw = service.SwitchServer(new SwitchRequest(clientId, server.Id, force));
        if (!sw.Succeeded)
            return Fail(sw.Error!);
        var previous = sw.Value!.Previous?.Name;
        SafeWriteLine(output, $"Switched to '{server.Name}'" + (previous is null || previous == server.Name ? "" : $" (previous: '{previous}')"));
        return 0;
    }

    private int Undo(Parsed parsed, PortalService service, string? explicitClientDir)
    {
        Expect(parsed, 0, 0, "no arguments");
        // Only an explicit --client-dir scopes undo to that client's last switch (#149).
        // REALM_PORTAL_CLIENT_DIR alone keeps option-free undo on the global record.
        // Resolve the stored id from the trusted path without AddClient / reading Wow.exe.
        string? clientId = null;
        if (explicitClientDir is not null)
        {
            clientId = ResolveRegisteredClientId(service, explicitClientDir);
            if (clientId is null)
                return Fail(new ServiceError(ServiceErrorCode.ClientNotFound));
        }

        var result = service.Undo(clientId);
        if (!result.Succeeded)
            return Fail(result.Error!);
        var restored = result.Value!.Active;
        SafeWriteLine(output, restored is null ? "Undone: no active server." : $"Undone: restored '{restored.Name}'.");
        // Core hands over one entry per line, so a newline inside a client path stays escaped.
        if (result.Value!.ClientFilesNoticeLines is { } lines)
            foreach (var line in lines)
                SafeWriteLine(output, line);
        return 0;
    }

    /// <summary>
    /// Maps a client folder to its already-registered id without calling <see cref="PortalService.AddClient"/>
    /// (no Wow.exe / version read). Returns null when no stored client matches the path.
    /// </summary>
    private static string? ResolveRegisteredClientId(PortalService service, string clientDir)
    {
        string id;
        try
        {
            id = ClientIds.FromFolderPath(clientDir);
        }
        catch (Exception)
        {
            return null;
        }

        var listed = service.ListClients();
        if (!listed.Succeeded)
            return null;
        return listed.Value!.Any(c => string.Equals(c.Id, id, StringComparison.Ordinal)) ? id : null;
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
            SafeWriteLine(output, "Active server: none");
        }
        else
        {
            var s = status.Active;
            SafeWriteLine(output, $"Active server: {s.Name} ({s.Id})");
            SafeWriteLine(output, $"Host: {s.Host}");
            SafeWriteLine(output, $"Realm: {(s.Realm.Length == 0 ? "-" : s.Realm)}");
            SafeWriteLine(output, $"Required client build: {s.RequiredClientBuild?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "any"}");
        }

        SafeWriteLine(output, $"activo.json: {(status.ProjectionInSync ? "in sync" : "OUT OF SYNC (run switch to repair)")}");
        if (status.ClientsNotMatchingActive.Count > 0 && status.Active is not null)
        {
            foreach (var client in status.ClientsNotMatchingActive)
                SafeWriteLine(output, ClientSyncMessages.DriftLine(status.Active.Name, client.Path));
        }

        SafeWriteLine(output, $"Can undo: {(status.CanUndo ? "yes" : "no")}");
        if (clientDir is not null)
        {
            try
            {
                var client = versionReader.Read(clientDir);
                var compat = status.Active is null ? "" : CompatibilityFilter.IsCompatible(status.Active, client) ? " (compatible)" : " (INCOMPATIBLE)";
                SafeWriteLine(output, $"Client: {client}{compat}");
            }
            catch (ClientVersionException ex)
            {
                SafeWriteLine(error, $"error: {ex.Message}");
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
                return DryRun(service, clientDir, wrapper, server.Name);

            var play = service.Play(new SwitchRequest(clientId, server.Id, force));
            if (!play.Succeeded)
                return FailLaunch(play.Error!, server.Name, service);
            SafeWriteLine(output, $"Launched {GameExecutable.Find(clientDir)} for '{server.Name}'.");
            return 0;
        }

        var status = service.GetStatus();
        if (!status.Succeeded)
            return Fail(status.Error!);
        server = status.Value!.Active ?? throw new InvalidOperationException("no active server; run 'switch <server>' or 'launch <server>'.");
        if (!server.IsCompatible && !force)
        {
            SafeWriteLine(error, $"error: '{server.Name}' requires client build {server.RequiredClientBuild}, but the client is incompatible (use --force to launch anyway).");
            return 2;
        }

        if (dryRun)
            return DryRun(service, clientDir, wrapper, server.Name);

        var launch = service.Play(new SwitchRequest(clientId, server.Id, force));
        if (!launch.Succeeded)
            return FailLaunch(launch.Error!, server.Name, service);
        SafeWriteLine(output, $"Launched {GameExecutable.Find(clientDir)} for '{server.Name}'.");
        return 0;
    }

    /// <summary>
    /// <c>launch --dry-run</c> (#130): resolves the same <see cref="GameLaunchSpec"/> a real launch would start (saved launcher
    /// settings, with <paramref name="wrapper"/> as the prefix when given, without saving it) and prints the resulting command,
    /// one quoted token per argument, plus any environment changes. A setting the CLI launcher cannot apply fails the same way a
    /// real launch would. Nothing is switched, saved or started.
    /// </summary>
    private int DryRun(PortalService service, string clientDir, string? wrapper, string serverName)
    {
        var executable = GameExecutable.Find(clientDir)
                         ?? throw new UsageException($"{GameExecutable.FileName} not found in '{clientDir}'.");
        var settings = service.GetLauncherSettings();
        if (!settings.Succeeded)
            return Fail(settings.Error!);
        var effective = wrapper is null ? settings.Value! : settings.Value! with { Prefix = [wrapper] };
        var spec = effective.ToSpec(executable, clientDir);
        if (GameLauncherAdapter.UnsupportedSetting(spec) is { } unsupported)
            return Fail(new ServiceError(ServiceErrorCode.InvalidLauncherSettings, Diagnostic: $"Unsupported launcher setting '{unsupported}'."));

        ProcessLaunch process;
        try
        {
            process = spec.Resolve();
        }
        catch (ArgumentException ex)
        {
            return Fail(new ServiceError(ServiceErrorCode.InvalidLauncherSettings, Diagnostic: ex.Message));
        }

        var command = string.Join(' ', new[] { process.FileName }.Concat(process.Arguments).Select(QuoteForDisplay));
        SafeWriteLine(output, $"Dry run: would launch {command} for '{serverName}'.");
        foreach (var (name, value) in process.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            SafeWriteLine(output, value is null ? $"  unset {name}" : $"  env {name}={value}");
        return 0;
    }

    /// <summary>Shows a token as one word: unchanged when it has no whitespace or quotes, otherwise in double quotes.</summary>
    internal static string QuoteForDisplay(string token) =>
        token.Length > 0 && !token.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'')
            ? token
            : "\"" + token.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

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
            SafeWriteLine(error, $"error: no server matches '{selector}'.");
            return 2;
        }

        server = found;
        return 0;
    }

    /// <summary>Escapes control characters so untrusted text cannot inject terminal lines or escape sequences.</summary>
    internal static string EscapeForTerminal(string value)
    {
        var safe = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsControl(character))
                safe.Append("\\u").Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            else
                safe.Append(character);
        }
        return safe.ToString();
    }

    /// <summary>Writes one line after escaping control characters. Shared with <see cref="Program"/>.</summary>
    internal static void SafeWriteLine(TextWriter writer, string value) =>
        writer.WriteLine(EscapeForTerminal(value));
}
