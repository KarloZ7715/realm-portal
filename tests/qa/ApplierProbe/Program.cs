using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;

// applier-probe plan|apply|undo --client <dir> --backup <dir> [--install <id>] [--realmlist <text with \r\n escapes>] [--realm <name>]
// Exit codes: 0 ok, 2 ClientFilesException (the code is printed as "ERROR <Code>"), 3 conflicts on undo, 1 usage.
// SAFETY: refuses to run when --client or --backup is not under the system temp directory.
// Only plan, apply and undo are accepted. Anything else (including a missing or misspelled command such as "aply")
// is rejected here, before any option parsing or planning, so an exit-code-based caller can never mistake it for success.
if (args.Length < 1 || args[0] is not ("plan" or "apply" or "undo"))
{
    Console.Error.WriteLine($"unknown command: {(args.Length < 1 ? "(none)" : args[0])}");
    Console.Error.WriteLine("usage: applier-probe plan|apply|undo --client DIR --backup DIR [--install ID] [--realmlist TEXT] [--realm NAME]");
    return 1;
}

var opts = new Dictionary<string, string>();
for (var i = 1; i + 1 < args.Length; i += 2)
    opts[args[i]] = args[i + 1];

string Need(string key) => opts.TryGetValue(key, out var v) ? v : throw new ArgumentException($"missing {key}");

try
{
    // Directory-boundary check. Path.GetFullPath alone does not resolve symlinks, so "<temp>/link/client" with link -> /elsewhere
    // would pass. Both sides are therefore resolved component by component: every existing symlink (absolute or relative target,
    // chained links included) is followed, ".." is applied after resolution like the OS does, and the part of a path that does not
    // exist yet (e.g. a new backup dir) is appended unresolved to its resolved existing ancestor. The temp root is resolved the same
    // way (on macOS /tmp -> /private/tmp). Then the temp root itself or "<root>/" must be a prefix, so sibling paths such as
    // /tmp-work or /tmp/work-evil (for TMPDIR=/tmp/work) are rejected too. Any resolution error fails closed (exit 1).
    string tmp, client, backup;
    try
    {
        tmp = ResolveSymlinks(Path.GetTempPath());
        client = ResolveSymlinks(Need("--client"));
        backup = ResolveSymlinks(Need("--backup"));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"refusing to run: cannot resolve paths safely ({ex.Message})");
        return 1;
    }
    var tmpPrefix = tmp + Path.DirectorySeparatorChar;
    foreach (var (name, p) in new[] { ("--client", client), ("--backup", backup) })
    {
        if (p != tmp && !p.StartsWith(tmpPrefix, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"refusing to touch {name} '{p}' (symlinks resolved): not under {tmp}");
            return 1;
        }
    }

    var install = opts.GetValueOrDefault("--install", "qa-install");
    var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new GameProcessProbe(), backup);

    if (args[0] == "undo")
    {
        var undone = applier.Undo(install);
        foreach (var f in undone.Files)
            Console.WriteLine($"{f.Status} {f.RelativePath}");
        Console.WriteLine($"BACKUP {undone.BackupId}");
        return undone.HasConflicts ? 3 : 0;
    }

    var version = new PeClientVersionReader().Read(client);
    var realmlist = Need("--realmlist").Replace("\\r\\n", "\r\n").Replace("\\n", "\n");
    var plan = applier.Plan(new ClientFilesRequest(install, "qa-profile", client, version, realmlist, opts.GetValueOrDefault("--realm", "")));
    foreach (var e in plan.Edits)
        Console.WriteLine($"PLAN {e.Kind} {e.RelativePath}");
    if (args[0] == "apply")
    {
        var result = applier.Apply(plan);
        Console.WriteLine($"APPLIED backup={result.BackupId ?? "none"} edits={result.Edits.Count}");
    }

    return 0;
}
catch (ClientFilesException ex)
{
    Console.WriteLine($"ERROR {ex.Code} path={ex.Path} :: {ex.Message}");
    return 2;
}
catch (Exception ex) when (ex is ArgumentException or ClientVersionException)
{
    Console.WriteLine($"ERROR {ex.GetType().Name} :: {ex.Message}");
    return 1;
}

// Returns the absolute, symlink-free form of `path`. Existing components are resolved through their link targets
// (relative targets are taken relative to the link's directory); components that do not exist are appended as-is.
// Throws IOException on a symlink loop (more than 40 links followed).
static string ResolveSymlinks(string path)
{
    var hops = 0;
    var absolute = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
    return Path.TrimEndingDirectorySeparator(Walk(absolute));

    string Walk(string abs)
    {
        var root = Path.GetPathRoot(abs)!;
        var current = root;
        var parts = abs[root.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part == ".")
                continue;
            if (part == "..")
            {
                current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)) ?? root;
                continue;
            }
            var candidate = Path.Combine(current, part);
            var target = new FileInfo(candidate).LinkTarget;
            if (target != null)
            {
                if (++hops > 40)
                    throw new IOException($"too many symbolic links while resolving '{path}'");
                current = Walk(Path.IsPathRooted(target) ? target : Path.Combine(current, target));
            }
            else if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                current = candidate;
            }
            else
            {
                // First missing component: nothing below it can be a symlink yet, so keep the rest unresolved.
                foreach (var rest in parts[i..])
                    current = rest == ".." ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)) ?? root
                            : rest == "." ? current : Path.Combine(current, rest);
                break;
            }
        }
        return current;
    }
}
