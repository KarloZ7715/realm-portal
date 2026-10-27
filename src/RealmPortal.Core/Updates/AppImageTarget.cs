using System.Diagnostics;
using System.Globalization;

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
    public static AppImageIdentityProbe? IdentityProbeForTests { get; set; }

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
        if (OperatingSystem.IsWindows())
            return false;

        try
        {
            // %d device, %i inode, %f raw mode hex, %u uid — GNU coreutils / BusyBox compatible enough for CI.
            var psi = new ProcessStartInfo
            {
                FileName = "stat",
                ArgumentList = { "-c", "%d %i %f %u", path },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return false;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            if (proc.ExitCode != 0)
                return false;
            var parts = output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                return false;
            if (!ulong.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out device))
                return false;
            if (!ulong.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out inode))
                return false;
            if (!int.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rawMode))
                return false;
            if (!uint.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid))
                return false;

            // S_IFREG = 0100000 octal = 0x8000
            if ((rawMode & 0xF000) != 0x8000)
                return false;
            mode = rawMode & 0x1FF;
            ownedByUser = uid == GetEuid();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static uint GetEuid()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "id",
                ArgumentList = { "-u" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return uint.MaxValue;
            var text = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(2000);
            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid) ? uid : uint.MaxValue;
        }
        catch
        {
            return uint.MaxValue;
        }
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
