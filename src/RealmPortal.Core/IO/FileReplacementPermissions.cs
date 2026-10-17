namespace RealmPortal.Core.IO;

/// <summary>Permission bits to preserve when atomically replacing an existing file.</summary>
public sealed record FileReplacementPermissions(int? UnixMode, bool? WindowsReadOnly)
{
    internal const int UnixPermissionMask = 0x1FF;

    public static FileReplacementPermissions? FromUnixAndWindows(int? unixMode, bool? windowsReadOnly)
    {
        if (unixMode is null && windowsReadOnly is null)
            return null;
        int? unix = null;
        if (unixMode is int u && TryNormalizeUnixMode(u, out var normalized))
            unix = normalized;
        var windows = OperatingSystem.IsWindows() ? windowsReadOnly : null;
        if (unix is null && windows is null)
            return null;
        return new FileReplacementPermissions(unix, windows);
    }

    /// <summary>Maps a stored mode to permission bits for <see cref="File.SetUnixFileMode"/>; rejects out-of-range values.</summary>
    internal static bool TryNormalizeUnixMode(int unix, out int normalized)
    {
        normalized = 0;
        if (unix < 0)
            return false;
        if (unix > UnixPermissionMask)
        {
            if (unix > 0xFFFF)
                return false;
            unix &= UnixPermissionMask;
        }

        normalized = unix;
        return true;
    }
}
