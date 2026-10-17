using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Clients;

/// <summary>Validates registered client folders before they are trusted.</summary>
public static class ClientPathSecurity
{
    /// <summary>True when <paramref name="directoryPath"/> is a reparse point or symlink (not a trusted client root).</summary>
    public static bool IsReparsePointOrSymlink(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!Directory.Exists(directoryPath))
            return false;

        var attributes = File.GetAttributes(directoryPath);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>Canonical folder path that must match <see cref="ClientIds.FromFolderPath"/> for the stored id.</summary>
    public static string CanonicalizeTrustedFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var full = Path.GetFullPath(folderPath);
        if (IsReparsePointOrSymlink(full))
            throw new StoreException($"Client folder '{full}' is a symbolic link or reparse point and cannot be registered.");
        return full;
    }
}
