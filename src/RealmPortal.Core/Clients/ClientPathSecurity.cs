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

    /// <summary>Rejects paths with a symlink or reparse point on any path prefix (including parents).</summary>
    public static void EnsureTrustedDirectoryPath(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var full = Path.GetFullPath(directoryPath);
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current) && IsReparsePointOrSymlink(current))
                throw new StoreException($"Path '{full}' cannot be used because '{current}' is a symbolic link or reparse point.");
        }
    }

    /// <summary>Canonical folder path that must match <see cref="ClientIds.FromFolderPath"/> for the stored id.</summary>
    public static string CanonicalizeTrustedFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var full = Path.GetFullPath(folderPath);
        EnsureTrustedDirectoryPath(full);
        if (!Directory.Exists(full))
            throw new StoreException($"Client folder '{full}' does not exist.");
        return full;
    }
}
