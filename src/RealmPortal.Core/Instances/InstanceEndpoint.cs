using System.Security.Cryptography;
using System.Text;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.Instances;

/// <summary>Where the Unix socket of an <see cref="InstanceEndpoint"/> lives.</summary>
public enum SocketLocation
{
    /// <summary>No socket (Windows named pipe, or no candidate path fits).</summary>
    None,

    /// <summary>Inside the instance directory itself (normal case).</summary>
    InstanceDirectory,

    /// <summary><c>$XDG_RUNTIME_DIR</c>, a per-user 0700 directory managed by the session.</summary>
    RuntimeDirectory,

    /// <summary>A <c>realm-portal-…</c> folder in the shared temp directory; must be private (0700, not a symlink).</summary>
    PrivateTempDirectory,
}

/// <summary>
/// Where one single-instance scope lives: a lock file in <see cref="Directory"/> plus a channel address (a Unix domain
/// socket path, or a Windows named pipe name). Two launches with the same directory and name are the same instance;
/// a different config directory (for example <c>REALM_PORTAL_CONFIG_DIR</c> in tests) is a separate instance.
/// </summary>
public sealed class InstanceEndpoint
{
    /// <summary>
    /// Longest socket path used, in UTF-8 bytes without the terminating NUL. <c>sun_path</c> is 108 bytes on Linux and
    /// 104 on macOS including the NUL (so 107 / 103 usable); 100 keeps a margin on both. Every candidate location is
    /// checked against this same limit.
    /// </summary>
    public const int MaxSocketPathBytes = 100;

    private InstanceEndpoint(string directory, string name, string lockPath, string? channelAddress, SocketLocation location)
    {
        Directory = directory;
        Name = name;
        LockPath = lockPath;
        ChannelAddress = channelAddress;
        Location = location;
    }

    /// <summary>Full path of the directory that holds the lock file (and, normally, the socket).</summary>
    public string Directory { get; }

    /// <summary>Instance name, such as <c>app</c>.</summary>
    public string Name { get; }

    /// <summary>The lock file; whoever holds it open exclusively is the primary instance.</summary>
    public string LockPath { get; }

    /// <summary>
    /// Socket path (Unix) or pipe name (Windows). Null when no candidate socket path fits
    /// <see cref="MaxSocketPathBytes"/>; the lock still works, but a second launch cannot hand over its request.
    /// </summary>
    public string? ChannelAddress { get; }

    /// <summary>Which candidate location the socket uses (Unix).</summary>
    public SocketLocation Location { get; }

    /// <summary>True when <see cref="ChannelAddress"/> is a named pipe name rather than a socket path.</summary>
    public bool UsesNamedPipe => OperatingSystem.IsWindows();

    /// <param name="directory">Directory for the lock file, usually the config directory.</param>
    /// <param name="name">Instance name: lowercase letters, digits and '-', 1 to 32 characters.</param>
    /// <param name="getVariable">
    /// Environment lookup for the long-path fallbacks: <c>XDG_RUNTIME_DIR</c>, and <c>TMPDIR</c> (the temp root; when
    /// unset, <see cref="Path.GetTempPath"/>).
    /// </param>
    public static InstanceEndpoint For(string directory, string name = "app", Func<string, string?>? getVariable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!InstanceProtocol.IsValidVerb(name))
            throw new ArgumentException($"'{name}' is not a valid instance name.", nameof(name));

        var full = Path.GetFullPath(directory);
        var lockPath = Path.Combine(full, $".realm-portal-{name}.lock");
        if (OperatingSystem.IsWindows())
            return new InstanceEndpoint(full, name, lockPath, $"RealmPortal-{name}-{Hash(full.ToUpperInvariant() + "|" + Environment.UserName)}", SocketLocation.None);

        foreach (var (path, location) in SocketCandidates(full, name, getVariable ?? Environment.GetEnvironmentVariable))
        {
            if (FitsSocketPath(path))
                return new InstanceEndpoint(full, name, lockPath, path, location);
        }
        return new InstanceEndpoint(full, name, lockPath, null, SocketLocation.None);
    }

    /// <summary>True when <paramref name="path"/> is at most <see cref="MaxSocketPathBytes"/> UTF-8 bytes.</summary>
    public static bool FitsSocketPath(string path) => Encoding.UTF8.GetByteCount(path) <= MaxSocketPathBytes;

    /// <summary>
    /// Socket paths in order of preference: inside the directory; <c>$XDG_RUNTIME_DIR/realm-portal-…sock</c>; a private
    /// <c>realm-portal-…</c> folder under the temp root. The file names carry a hash of the directory, so different
    /// directories never share a socket.
    /// </summary>
    internal static IEnumerable<(string Path, SocketLocation Location)> SocketCandidates(string fullDirectory, string name, Func<string, string?> getVariable)
    {
        yield return (Path.Combine(fullDirectory, $".realm-portal-{name}.sock"), SocketLocation.InstanceDirectory);

        var id = Hash(fullDirectory);
        var runtime = getVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtime) && Path.IsPathFullyQualified(runtime))
            yield return (Path.Combine(runtime, $"realm-portal-{name}-{id}.sock"), SocketLocation.RuntimeDirectory);

        yield return (Path.Combine(TempRoot(getVariable), $"realm-portal-{Hash(Environment.UserName)}", $"{name}-{id}.sock"), SocketLocation.PrivateTempDirectory);
    }

    private static string TempRoot(Func<string, string?> getVariable)
    {
        var tmp = getVariable("TMPDIR");
        return !string.IsNullOrWhiteSpace(tmp) && Path.IsPathFullyQualified(tmp)
            ? Path.TrimEndingDirectorySeparator(tmp)
            : Path.TrimEndingDirectorySeparator(Path.GetTempPath());
    }

    /// <summary>
    /// Primary side: creates the private temp folder (0700) when the socket lives there, and proves it is ours.
    /// No-op for the other locations.
    /// </summary>
    internal void EnsureSocketDirectory()
    {
        if (Location != SocketLocation.PrivateTempDirectory || OperatingSystem.IsWindows())
            return;
        var parent = Path.GetDirectoryName(ChannelAddress)!;

        // A shared temp folder: another user may have created the folder first. Refuse a symlink, and set the mode to
        // 0700: changing the mode only succeeds for the owner, so success also proves ownership.
        var info = new DirectoryInfo(parent);
        if (info.LinkTarget is not null)
            throw new IOException($"'{parent}' is a symbolic link.");
        PrivateStorage.CreatePrivateDirectory(parent);
        File.SetUnixFileMode(parent, PrivateStorage.PrivateDirectoryMode);
        VerifyPrivateSocketDirectory();
    }

    /// <summary>
    /// Both sides: for the private temp folder, refuse to use it unless it is a real directory (not a symlink) with
    /// mode 0700. A 0700 folder owned by someone else cannot be entered, so this also rules out a socket planted by
    /// another user; on Linux the peer credentials are checked as well (see <see cref="SingleInstance"/>).
    /// </summary>
    internal void VerifyPrivateSocketDirectory()
    {
        if (Location != SocketLocation.PrivateTempDirectory || OperatingSystem.IsWindows())
            return;
        var parent = Path.GetDirectoryName(ChannelAddress)!;
        var info = new DirectoryInfo(parent);
        if (info.LinkTarget is not null)
            throw new IOException($"'{parent}' is a symbolic link.");
        if (!info.Exists)
            throw new IOException($"'{parent}' does not exist.");
        if (File.GetUnixFileMode(parent) != PrivateStorage.PrivateDirectoryMode)
            throw new UnauthorizedAccessException($"'{parent}' is not private (mode 0700); not using it.");
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
