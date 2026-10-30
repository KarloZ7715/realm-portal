using System.Security.Cryptography;
using System.Text;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.Instances;

/// <summary>
/// Where one single-instance scope lives: a lock file in <see cref="Directory"/> plus a channel address (a Unix domain
/// socket path, or a Windows named pipe name). Two launches with the same directory and name are the same instance;
/// a different config directory (for example <c>REALM_PORTAL_CONFIG_DIR</c> in tests) is a separate instance.
/// </summary>
public sealed class InstanceEndpoint
{
    /// <summary>
    /// Longest socket path used as is. <c>sun_path</c> holds 108 bytes on Linux and 104 on macOS, including the
    /// terminating NUL; longer config paths fall back to <see cref="FallbackSocketPath"/>.
    /// </summary>
    public const int MaxSocketPathBytes = 100;

    private InstanceEndpoint(string directory, string name, string lockPath, string? channelAddress)
    {
        Directory = directory;
        Name = name;
        LockPath = lockPath;
        ChannelAddress = channelAddress;
    }

    /// <summary>Full path of the directory that holds the lock file (and, normally, the socket).</summary>
    public string Directory { get; }

    /// <summary>Instance name, such as <c>app</c>.</summary>
    public string Name { get; }

    /// <summary>The lock file; whoever holds it open exclusively is the primary instance.</summary>
    public string LockPath { get; }

    /// <summary>
    /// Socket path (Unix) or pipe name (Windows). Null when no usable address exists (a socket path that is too long
    /// even in the fallback location); the lock still works, but a second launch cannot hand over its request.
    /// </summary>
    public string? ChannelAddress { get; }

    /// <summary>True when <see cref="ChannelAddress"/> is a named pipe name rather than a socket path.</summary>
    public bool UsesNamedPipe => OperatingSystem.IsWindows();

    /// <param name="directory">Directory for the lock file, usually the config directory.</param>
    /// <param name="name">Instance name: lowercase letters, digits and '-', 1 to 32 characters.</param>
    /// <param name="getVariable">Environment lookup, for <c>XDG_RUNTIME_DIR</c> in the long-path fallback.</param>
    public static InstanceEndpoint For(string directory, string name = "app", Func<string, string?>? getVariable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!InstanceProtocol.IsValidVerb(name))
            throw new ArgumentException($"'{name}' is not a valid instance name.", nameof(name));

        var full = Path.GetFullPath(directory);
        var lockPath = Path.Combine(full, $".realm-portal-{name}.lock");
        if (OperatingSystem.IsWindows())
            return new InstanceEndpoint(full, name, lockPath, $"RealmPortal-{name}-{Hash(full.ToUpperInvariant() + "|" + Environment.UserName)}");

        var socket = Path.Combine(full, $".realm-portal-{name}.sock");
        if (Encoding.UTF8.GetByteCount(socket) > MaxSocketPathBytes)
            socket = FallbackSocketPath(full, name, getVariable ?? Environment.GetEnvironmentVariable);
        return new InstanceEndpoint(full, name, lockPath, socket is not null && Encoding.UTF8.GetByteCount(socket) <= MaxSocketPathBytes ? socket : null);
    }

    /// <summary>
    /// Short socket path for a config directory whose own path is too long: <c>$XDG_RUNTIME_DIR/realm-portal-…sock</c>
    /// (a per-user 0700 directory by spec), else a private <c>realm-portal-…</c> folder under the temp path.
    /// The file name carries a hash of the config directory, so different directories never share a socket.
    /// </summary>
    internal static string? FallbackSocketPath(string fullDirectory, string name, Func<string, string?> getVariable)
    {
        var id = Hash(fullDirectory);
        var runtime = getVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtime) && Path.IsPathFullyQualified(runtime))
            return Path.Combine(runtime, $"realm-portal-{name}-{id}.sock");

        // The private folder is created (and checked) by the primary instance when it binds; see SingleInstance.
        return Path.Combine(Path.GetTempPath(), $"realm-portal-{Hash(Environment.UserName)}", $"{name}-{id}.sock");
    }

    /// <summary>Creates the socket's parent folder as private (0700) when it is our own fallback folder under the temp path.</summary>
    internal void EnsureSocketDirectory()
    {
        if (ChannelAddress is null || OperatingSystem.IsWindows())
            return;
        var parent = Path.GetDirectoryName(ChannelAddress)!;
        if (string.Equals(parent, Directory, StringComparison.Ordinal))
            return; // The config directory itself, already created by the lock.

        var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        if (!string.Equals(Path.GetDirectoryName(parent), tempRoot, StringComparison.Ordinal))
            return; // $XDG_RUNTIME_DIR: owned and protected by the session.

        // A shared temp folder: someone else may have created the folder first. Refuse a symlink, and make sure the
        // mode is 0700 (changing the mode only succeeds for the owner, so success also proves ownership).
        var info = new DirectoryInfo(parent);
        if (info.Exists && info.LinkTarget is not null)
            throw new IOException($"'{parent}' is a symbolic link.");
        PrivateStorage.CreatePrivateDirectory(parent);
        File.SetUnixFileMode(parent, PrivateStorage.PrivateDirectoryMode);
        if (File.GetUnixFileMode(parent) != PrivateStorage.PrivateDirectoryMode)
            throw new IOException($"'{parent}' is not private.");
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
