using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;

// applier-probe plan|apply|undo --client <dir> --backup <dir> [--install <id>] [--realmlist <text with \r\n escapes>] [--realm <name>]
// Exit codes: 0 ok, 2 ClientFilesException (the code is printed as "ERROR <Code>"), 3 conflicts on undo, 1 usage.
// SAFETY: refuses to run when --client or --backup is not under the system temp directory.
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: applier-probe plan|apply|undo --client DIR --backup DIR [--install ID] [--realmlist TEXT] [--realm NAME]");
    return 1;
}

var opts = new Dictionary<string, string>();
for (var i = 1; i + 1 < args.Length; i += 2)
    opts[args[i]] = args[i + 1];

string Need(string key) => opts.TryGetValue(key, out var v) ? v : throw new ArgumentException($"missing {key}");

try
{
    // Directory-boundary check: normalize both sides and require the temp root itself or "<root>/" as a prefix,
    // so sibling paths such as /tmp-work or /tmp/work-evil (for TMPDIR=/tmp/work) are rejected.
    var tmp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
    var tmpPrefix = tmp + Path.DirectorySeparatorChar;
    bool IsUnderTemp(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return full == tmp || full.StartsWith(tmpPrefix, StringComparison.Ordinal);
    }
    var client = Path.GetFullPath(Need("--client"));
    var backup = Path.GetFullPath(Need("--backup"));
    foreach (var p in new[] { client, backup })
    {
        if (!IsUnderTemp(p))
        {
            Console.Error.WriteLine($"refusing to touch '{p}': not under {tmp}");
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
