namespace RealmPortal.Core.Updates;

/// <summary>
/// Validates <c>$APPIMAGE</c> for in-place replacement (docs/updates-and-status.md).
/// When any check fails, callers fall back to a verified download into Downloads (no replacement).
/// </summary>
public sealed record AppImageTargetInfo(
    string ResolvedPath,
    ulong Device,
    ulong Inode,
    int OriginalMode);

public delegate bool AppImageIdentityProbe(
    string path,
    out ulong device,
    out ulong inode,
    out int mode,
    out bool ownedByUser);

public static class AppImageTarget
{
    private static readonly byte[] ElfMagic = [0x7F, (byte)'E', (byte)'L', (byte)'F'];
    private static readonly byte[] AppImageType2 = [(byte)'A', (byte)'I', 0x02];

    /// <summary>Test seam for (device, inode, mode, ownership).</summary>
    internal static AppImageIdentityProbe? IdentityProbeForTests { get; set; }

    public static bool TryResolve(
        Func<string, string?>? getVariable,
        out AppImageTargetInfo info,
        out string? reason)
    {
        info = null!;
        reason = null;
        var get = getVariable ?? Environment.GetEnvironmentVariable;
        var appImage = get("APPIMAGE");
        if (string.IsNullOrWhiteSpace(appImage))
        {
            reason = "APPIMAGE is not set.";
            return false;
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(appImage);
            if (IsSymlink(resolved))
            {
                var target = ResolveSymlink(resolved);
                if (target is null)
                {
                    reason = "APPIMAGE symlink cannot be resolved.";
                    return false;
                }

                resolved = Path.GetFullPath(target);
            }
        }
        catch (Exception ex)
        {
            reason = "APPIMAGE path invalid: " + ex.Message;
            return false;
        }

        if (!Path.IsPathRooted(resolved) || !File.Exists(resolved))
        {
            reason = "APPIMAGE is not an absolute existing file.";
            return false;
        }

        if (IsSymlink(resolved) || Directory.Exists(resolved))
        {
            reason = "APPIMAGE target must be a regular file.";
            return false;
        }

        try
        {
            using var stream = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[11];
            var read = stream.Read(header);
            if (read < 11
                || !header[..4].SequenceEqual(ElfMagic)
                || !header[8..11].SequenceEqual(AppImageType2))
            {
                reason = "APPIMAGE lacks ELF + AppImage type 2 magic.";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason = "APPIMAGE cannot be read: " + ex.Message;
            return false;
        }

        var dir = Path.GetDirectoryName(resolved);
        if (string.IsNullOrEmpty(dir) || !IsDirectoryWritable(dir))
        {
            reason = "APPIMAGE directory is not writable.";
            return false;
        }

        if (!TryGetIdentity(resolved, out var device, out var inode, out var mode, out var ownedByUser)
            || !ownedByUser)
        {
            reason = "APPIMAGE is not a regular file owned by the current user.";
            return false;
        }

        info = new AppImageTargetInfo(resolved, device, inode, mode);
        return true;
    }

    public static bool SameIdentity(string path, AppImageTargetInfo expected)
    {
        if (!TryGetIdentity(path, out var device, out var inode, out _, out var owned) || !owned)
            return false;
        return device == expected.Device && inode == expected.Inode;
    }

    /// <summary>
    /// True when no other user can rename, delete or create entries in <paramref name="directory"/>
    /// or any ancestor (#206). See <see cref="UnixPathSafety.IsDirectoryTreeSafe"/>.
    /// </summary>
    public static bool IsDirectorySafeForReplace(string directory, out string? reason) =>
        UnixPathSafety.IsDirectoryTreeSafe(directory, out reason);

    /// <summary>
    /// True when the AppImage at <paramref name="path"/> is a regular file owned by the current user that no other user
    /// can write (#206). Group-write is allowed only for a strict private group.
    /// </summary>
    public static bool IsFileSafeForReplace(string path, out string? reason) =>
        UnixPathSafety.IsRegularFileSafe(path, out reason);

    /// <summary>File type and permission bits (<c>st_mode</c>) and ownership, without following a final symlink.</summary>
    internal static bool TryStat(string path, out int rawMode, out bool ownedByUser)
    {
        rawMode = 0;
        ownedByUser = false;
        if (!UnixPathSafety.TryLstat(path, out var st))
            return false;
        rawMode = st.RawMode;
        ownedByUser = st.OwnedByUser;
        return true;
    }

    /// <summary>Original mode masked with 0755 (drop setuid/setgid/sticky and group/other write).</summary>
    public static int ModeMasked0755(int originalMode)
    {
        const int mask = 0b111_101_101; // 0755
        return originalMode & mask & 0x1FF;
    }

    public static bool TryGetIdentity(string path, out ulong device, out ulong inode, out int mode, out bool ownedByUser)
    {
        if (IdentityProbeForTests is { } probe)
            return probe(path, out device, out inode, out mode, out ownedByUser);

        device = 0;
        inode = 0;
        mode = 0;
        ownedByUser = false;
        if (!UnixPathSafety.TryLstat(path, out var st) || !st.IsRegularFile)
            return false;
        device = st.Device;
        inode = st.Inode;
        mode = st.Permissions & 0x1FF;
        ownedByUser = st.OwnedByUser;
        return true;
    }

    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".realm-portal-write-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveSymlink(string path)
    {
        try
        {
            var current = path;
            for (var i = 0; i < 40; i++)
            {
                var fi = new FileInfo(current);
                if (fi.LinkTarget is null)
                    return Path.GetFullPath(current);
                var next = fi.LinkTarget;
                if (!Path.IsPathRooted(next))
                    next = Path.Combine(Path.GetDirectoryName(current) ?? "", next);
                current = Path.GetFullPath(next);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
