namespace RealmPortal.Core.IO;

/// <summary>Permission bits to preserve when atomically replacing an existing file.</summary>
public sealed record FileReplacementPermissions(int? UnixMode, bool? WindowsReadOnly)
{
    internal const int UnixPermissionMask = 0x1FF;

    /// <summary>
    /// Builds the permissions to copy onto a replacement file. <paramref name="unixMode"/> must already be plain permission bits
    /// (0..0777): setuid, setgid and sticky bits are never copied, and a value outside the range is rejected rather than masked.
    /// </summary>
    public static FileReplacementPermissions? FromUnixAndWindows(int? unixMode, bool? windowsReadOnly)
    {
        if (unixMode is int u && !IsValidUnixMode(u))
            throw new ArgumentOutOfRangeException(nameof(unixMode), u, "A Unix mode must be within 0..0777.");
        var windows = OperatingSystem.IsWindows() ? windowsReadOnly : null;
        if (unixMode is null && windows is null)
            return null;
        return new FileReplacementPermissions(unixMode, windows);
    }

    /// <summary>True for plain permission bits, 0..0777 (no file type, setuid, setgid or sticky bits).</summary>
    public static bool IsValidUnixMode(int mode) => mode is >= 0 and <= UnixPermissionMask;
}
