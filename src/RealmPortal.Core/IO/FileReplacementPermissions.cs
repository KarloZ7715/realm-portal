namespace RealmPortal.Core.IO;

/// <summary>Permission bits to preserve when atomically replacing an existing file.</summary>
public sealed record FileReplacementPermissions(int? UnixMode, bool? WindowsReadOnly)
{
    internal const int UnixPermissionMask = 0x1FF;

    public static FileReplacementPermissions? FromUnixAndWindows(int? unixMode, bool? windowsReadOnly)
    {
        if (unixMode is null && windowsReadOnly is null)
            return null;
        return new FileReplacementPermissions(
            unixMode is int u ? u & UnixPermissionMask : null,
            OperatingSystem.IsWindows() ? windowsReadOnly : null);
    }
}
