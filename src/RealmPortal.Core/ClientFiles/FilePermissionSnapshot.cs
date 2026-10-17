using RealmPortal.Core.IO;

namespace RealmPortal.Core.ClientFiles;

/// <summary>Captured permission bits of an existing client file before replace.</summary>
internal readonly record struct FilePermissionSnapshot(int? UnixMode, bool? WindowsReadOnly)
{
    public static FilePermissionSnapshot? Capture(string path)
    {
        if (!File.Exists(path))
            return null;

        int? unix = null;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            unix = (int)File.GetUnixFileMode(path) & FileReplacementPermissions.UnixPermissionMask;

        bool? windowsReadOnly = OperatingSystem.IsWindows()
            ? File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly)
            : null;

        return new FilePermissionSnapshot(unix, windowsReadOnly);
    }

    public static bool IsReadOnly(string path)
    {
        if (!File.Exists(path))
            return false;
        if (OperatingSystem.IsWindows())
            return File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return (File.GetUnixFileMode(path) & UnixFileMode.UserWrite) == 0;
        return false;
    }

    public FileReplacementPermissions? ToReplacementPermissions() =>
        FileReplacementPermissions.FromUnixAndWindows(UnixMode, WindowsReadOnly);
}
