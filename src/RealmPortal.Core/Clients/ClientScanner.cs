using System.Runtime.Versioning;
using Microsoft.Win32;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Clients;

/// <summary>
/// Finds game client folders in the standard Wine, Lutris, Steam and plain-folder locations plus any extra root
/// the caller passes. Discovery is strictly read-only: candidate folders and launcher configurations are opened
/// without following links and with size caps, and nothing is ever created, modified or deleted. The scanner does
/// not register anything; it returns what it found so the caller can deduplicate against <c>clients.json</c>.
/// </summary>
public sealed class ClientScanner
{
    /// <summary>Descent budget for a root without a source-specific one.</summary>
    public const int DefaultMaxDepth = 3;

    /// <summary>Hard cap on inspected folders across a whole scan.</summary>
    public const int DefaultMaxDirectoriesVisited = 2000;

    /// <summary>Depth used for a Steam <c>compatdata</c> tree (<c>&lt;appid&gt;/pfx/drive_c/Program Files&lt;*&gt;/&lt;game&gt;</c>).</summary>
    private const int SteamCompatDataDepth = 6;

    private static readonly HashSet<string> SkippedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "system32", "syswow64", "winsxs", "$recycle.bin", "node_modules",
    };

    private readonly IClientVersionReader _versionReader;

    /// <summary>Creates a scanner; when no reader is given the real PE version reader is used (never runs the executable).</summary>
    public ClientScanner(IClientVersionReader? versionReader = null) =>
        _versionReader = versionReader ?? new PeClientVersionReader();

    /// <summary>
    /// Inspects the candidate locations and returns every client found, ordered by folder path. A folder already
    /// present in <see cref="ClientScanOptions.KnownClientIds"/> is skipped. When several sources reference the same
    /// folder in one scan, the origin with the highest precedence wins (Lutris &gt; Steam &gt; Wine &gt; Discovered).
    /// </summary>
    public IReadOnlyList<DiscoveredClient> Scan(ClientScanOptions? options = null, IProgress<ClientScanProgress>? progress = null)
    {
        var effective = options ?? ClientScanOptions.Default;
        var state = new ScanState(effective, progress);
        var found = new Dictionary<string, DiscoveredClient>(StringComparer.Ordinal);

        foreach (var root in CollectRoots(effective))
        {
            state.CurrentRoot = root.Path;
            Walk(root.Path, root.Origin, depth: 0, root.MaxDepth, state, found);
            if (state.BudgetExhausted)
                break;
        }

        return found.Values.OrderBy(client => client.Folder, StringComparer.Ordinal).ToList();
    }

    private void Walk(string directory, ClientOrigin origin, int depth, int maxDepth, ScanState state, Dictionary<string, DiscoveredClient> found)
    {
        if (state.BudgetExhausted || !state.CanInspect(directory) || !Directory.Exists(directory))
            return;

        // A hidden child ('.git', '.cache', ...) is never a client, but a configured root such as '~/.wine' may
        // itself be hidden, so the dot rule only applies below the root.
        var name = Path.GetFileName(directory);
        if (name.Length > 0 && (SkippedSegments.Contains(name) || (depth > 0 && name[0] == '.')))
            return;

        if (ClientPathSecurity.IsReparsePointOrSymlink(directory))
            return;

        if (!state.Visit())
            return;

        if (FindExecutable(directory) is not null)
        {
            AddCandidate(directory, origin, state, found);
            return;
        }

        if (depth >= maxDepth)
            return;

        foreach (var child in ChildDirectories(directory))
            Walk(child, origin, depth + 1, maxDepth, state, found);
    }

    private void AddCandidate(string directory, ClientOrigin origin, ScanState state, Dictionary<string, DiscoveredClient> found)
    {
        string canonical;
        try
        {
            canonical = ClientPathSecurity.CanonicalizeTrustedFolder(directory);
        }
        catch (Exception ex) when (ex is StoreException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }

        var id = ClientIds.FromFolderPath(canonical);
        if (state.KnownIds?.Contains(id) == true)
            return;
        if (found.TryGetValue(id, out var existing) && Precedence(existing.Origin) <= Precedence(origin))
            return;

        ClientVersion? version = null;
        ClientVersionErrorKind? error = null;
        try
        {
            version = _versionReader.Read(canonical);
        }
        catch (ClientVersionException ex)
        {
            error = ex.Kind;
        }
        catch (Exception)
        {
            error = ClientVersionErrorKind.Unreadable;
        }

        found[id] = new DiscoveredClient(id, canonical, version, error, origin);
        state.ClientsFound = found.Count;
        state.Report();
    }

    /// <summary>Lower is better: Lutris, then Steam, then Wine, then a plain discovered folder.</summary>
    private static int Precedence(ClientOrigin origin) => origin switch
    {
        ClientOrigin.Lutris => 0,
        ClientOrigin.Steam => 1,
        ClientOrigin.Wine => 2,
        ClientOrigin.Discovered => 3,
        _ => 4,
    };

    private static string? FindExecutable(string directory)
    {
        try
        {
            return GameExecutable.Find(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ChildDirectories(string directory)
    {
        List<string> children;
        try
        {
            children = Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }

        children.Sort(StringComparer.Ordinal);
        return children;
    }

    private static IEnumerable<string> ExistingChildDirectories(string directory) =>
        Directory.Exists(directory) ? ChildDirectories(directory) : Enumerable.Empty<string>();

    private static IEnumerable<string> ExistingChildFiles(string directory, string pattern)
    {
        List<string> files;
        try
        {
            files = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            files = [];
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    // ------------------------------------------------------------------ candidate roots

    private static IReadOnlyList<Root> CollectRoots(ClientScanOptions options)
    {
        var roots = new List<Root>();
        foreach (var extra in options.AdditionalRoots)
        {
            if (!string.IsNullOrWhiteSpace(extra))
                roots.Add(new Root(extra.Trim(), ClientOrigin.Discovered, options.MaxDepth));
        }

        if (!options.IncludeDefaultRoots)
            return roots;

        if (OperatingSystem.IsWindows())
        {
            AddWindowsRoots(roots);
            return roots;
        }

        var home = Home(options);
        if (home is null)
            return roots;

        if (Environment.GetEnvironmentVariable("WINEPREFIX") is { Length: > 0 } winePrefix)
            roots.Add(new Root(winePrefix, ClientOrigin.Wine, 4));
        roots.Add(new Root(Path.Combine(home, ".wine"), ClientOrigin.Wine, 4));

        foreach (var prefix in ExistingChildDirectories(Path.Combine(home, ".local", "share", "wineprefixes")))
            roots.Add(new Root(prefix, ClientOrigin.Wine, 4));
        foreach (var bottle in ExistingChildDirectories(Path.Combine(home, ".local", "share", "bottles", "bottles")))
            roots.Add(new Root(Path.Combine(bottle, "drive_c"), ClientOrigin.WinePrefix, 3));
        roots.Add(new Root(Path.Combine(home, ".local", "share", "lutris", "runners", "wine"), ClientOrigin.Wine, 3));

        foreach (var configDirectory in LutrisConfigDirectories(home))
        {
            foreach (var config in ExistingChildFiles(configDirectory, "*.yml"))
            {
                foreach (var directory in LutrisGameConfigs.ReadCandidateDirectories(config))
                    roots.Add(new Root(directory, ClientOrigin.Lutris, 2));
            }
        }

        foreach (var steamRoot in SteamRoots(home))
            AddSteamRoots(roots, steamRoot);

        roots.Add(new Root(Path.Combine(home, "Games"), ClientOrigin.Discovered, 3));
        roots.Add(new Root(Path.Combine(home, "Juegos"), ClientOrigin.Discovered, 3));
        roots.Add(new Root("/opt/games", ClientOrigin.Discovered, 3));

        return roots;
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsRoots(List<Root> roots)
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var path = Environment.GetFolderPath(folder);
            if (path.Length > 0)
                roots.Add(new Root(path, ClientOrigin.Discovered, 2));
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0)
            roots.Add(new Root(Path.Combine(profile, "Games"), ClientOrigin.Discovered, 3));

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed)
                    roots.Add(new Root(Path.Combine(drive.RootDirectory.FullName, "Games"), ClientOrigin.Discovered, 3));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A drive that cannot be queried is simply not a candidate.
        }

        foreach (var steamRoot in SteamRoots(home: null))
            AddSteamRoots(roots, steamRoot);

        AddBlizzardRegistryHints(roots);
    }

    /// <summary>Reads the Blizzard install keys (the documented registry hint for a World of Warcraft client).</summary>
    [SupportedOSPlatform("windows")]
    private static void AddBlizzardRegistryHints(List<Root> roots)
    {
        var probes = new (RegistryHive Hive, RegistryView View)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Default),
        };

        foreach (var (hive, view) in probes)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Blizzard Entertainment\World of Warcraft");
                if (key is null)
                    continue;

                foreach (var valueName in new[] { "InstallPath", "GamePath" })
                {
                    if (key.GetValue(valueName) is string value && value.Trim().Length > 0)
                        roots.Add(new Root(TrimTrailingSeparators(value.Trim()), ClientOrigin.Discovered, 2));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException
                                           or IOException or PlatformNotSupportedException or ArgumentException)
            {
                // No readable registry view: directory hints still apply.
            }
        }
    }

    private static void AddSteamRoots(List<Root> roots, string steamRoot)
    {
        var candidates = new List<string> { steamRoot };
        try
        {
            candidates.AddRange(SteamLibraryFolders.ReadLibraries(steamRoot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A malformed library file leaves only the installation's own steamapps folder.
        }

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            roots.Add(new Root(Path.Combine(candidate, "steamapps", "common"), ClientOrigin.Steam, 2));
            roots.Add(new Root(Path.Combine(candidate, "steamapps", "compatdata"), ClientOrigin.Steam, SteamCompatDataDepth));
        }
    }

    private static IEnumerable<string> SteamRoots(string? home)
    {
        if (OperatingSystem.IsWindows())
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (programFilesX86.Length > 0)
                yield return Path.Combine(programFilesX86, "Steam");
            yield break;
        }

        if (home is null)
            yield break;

        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".steam", "root");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        yield return Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam");
    }

    private static IEnumerable<string> LutrisConfigDirectories(string home)
    {
        yield return Path.Combine(home, ".config", "lutris", "games");
        yield return Path.Combine(home, ".local", "share", "lutris", "games");
    }

    private static string? Home(ClientScanOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.HomeDirectory))
            return options.HomeDirectory;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length == 0)
            home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        return home.Length > 0 ? home : null;
    }

    private static string TrimTrailingSeparators(string path) =>
        path.Length > 1 && (path[^1] == Path.DirectorySeparatorChar || path[^1] == Path.AltDirectorySeparatorChar)
            ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : path;

    private readonly record struct Root(string Path, ClientOrigin Origin, int MaxDepth);

    // ------------------------------------------------------------------ scan state

    private sealed class ScanState(ClientScanOptions options, IProgress<ClientScanProgress>? progress)
    {
        private readonly MountTable _mounts = MountTable.Load();

        public IReadOnlySet<string>? KnownIds { get; } = options.KnownClientIds;

        public int MaxDirectories { get; } = options.MaxDirectoriesVisited;

        public string CurrentRoot { get; set; } = string.Empty;

        public int Inspected { get; private set; }

        public int ClientsFound { get; set; }

        public bool BudgetExhausted => Inspected >= MaxDirectories;

        /// <summary>False for a folder on a network or special mount, which must never be entered.</summary>
        public bool CanInspect(string directory)
        {
            if (OperatingSystem.IsWindows())
                return !IsRemoteWindowsRoot(directory);
            return !_mounts.IsRemote(directory);
        }

        /// <summary>Counts one inspected folder; false when the scan budget is used up.</summary>
        public bool Visit()
        {
            if (BudgetExhausted)
                return false;

            Inspected++;
            Report();
            return true;
        }

        /// <summary>Reports the current counts.</summary>
        public void Report() => progress?.Report(new ClientScanProgress(CurrentRoot, Inspected, ClientsFound));

        private static bool IsRemoteWindowsRoot(string path)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root))
                    return true;
                var type = new DriveInfo(root).DriveType;
                return type is DriveType.Network or DriveType.CDRom or DriveType.NoRootDirectory;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return true;
            }
        }
    }

    /// <summary>Best-effort <c>/proc/self/mounts</c> lookup used to stay off network and special file systems.</summary>
    private sealed class MountTable
    {
        private static readonly HashSet<string> RemoteFileSystems = new(StringComparer.Ordinal)
        {
            "nfs", "nfs4", "cifs", "smb3", "smbfs", "sshfs", "fuse.sshfs", "fuse.rclone", "fuse.gvfsd-fuse",
            "fuse.davfs", "fuse.curlftpfs", "fuse.smbnetfs", "davfs", "davfs2", "9p", "ceph", "afs",
            "glusterfs", "autofs",
        };

        private readonly List<(string Path, string FileSystem)> _entries = [];

        public static MountTable Load()
        {
            var table = new MountTable();
            if (!OperatingSystem.IsLinux())
                return table;

            try
            {
                foreach (var line in File.ReadLines("/proc/self/mounts"))
                {
                    var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length >= 3)
                        table._entries.Add((Unescape(fields[1]), fields[2]));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Without a mount table no folder is treated as remote; the traversal bounds still apply.
            }

            return table;
        }

        public bool IsRemote(string path)
        {
            if (_entries.Count == 0)
                return false;

            var full = Path.GetFullPath(path);
            string? bestPath = null;
            var bestFileSystem = string.Empty;
            foreach (var (mountPath, fileSystem) in _entries)
            {
                if (!IsUnder(full, mountPath))
                    continue;
                if (bestPath is null || mountPath.Length >= bestPath.Length)
                {
                    bestPath = mountPath;
                    bestFileSystem = fileSystem;
                }
            }

            return bestPath is not null && RemoteFileSystems.Contains(bestFileSystem);
        }

        private static bool IsUnder(string path, string root) =>
            root == "/" || path == root || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal);

        /// <summary>Mount points escape a space as <c>\040</c>, a tab as <c>\011</c> and a backslash as <c>\134</c>.</summary>
        private static string Unescape(string value) => value
            .Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal)
            .Replace("\\012", "\n", StringComparison.Ordinal)
            .Replace("\\134", "\\", StringComparison.Ordinal);
    }
}
