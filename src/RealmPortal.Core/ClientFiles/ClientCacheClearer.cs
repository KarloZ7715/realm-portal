namespace RealmPortal.Core.ClientFiles;

/// <summary>
/// Clears WoW <c>Cache</c> and <c>WDB</c> folders under a client root. Path safety matches the applier:
/// only those two child names, never follow a symlink that escapes the client, and never delete outside it.
/// </summary>
public static class ClientCacheClearer
{
    /// <summary>Top-level client folders cleared when the setting is on (3.3.5 clients use <c>Cache</c>; some layouts also have <c>WDB</c>).</summary>
    public static readonly string[] FolderNames = ["Cache", "WDB"];

    /// <summary>
    /// Deletes each of <see cref="FolderNames"/> under <paramref name="clientRoot"/> when present and inside the client.
    /// Missing folders are ignored. Escaping symlinks raise <see cref="ClientFilesErrorCode.PathEscapesClient"/>.
    /// </summary>
    public static void Clear(string clientRoot)
    {
        var root = ClientPaths.CanonicalRoot(clientRoot);
        foreach (var name in FolderNames)
            ClearOne(root, name);
    }

    private static void ClearOne(string canonicalRoot, string childName)
    {
        var folder = ClientPaths.ResolveChildDirectoryInsideClient(canonicalRoot, childName);
        if (folder is null)
            return;

        DeleteTreeWithoutFollowingEscapingLinks(canonicalRoot, folder);
    }

    /// <summary>
    /// Removes <paramref name="directory"/> and its contents. Reparse points (symlinks/junctions) are deleted as
    /// the link node only — their targets are never walked — so a planted link inside Cache cannot delete outside.
    /// </summary>
    private static void DeleteTreeWithoutFollowingEscapingLinks(string canonicalRoot, string directory)
    {
        EnsureListedPathStaysUnderClient(canonicalRoot, directory);

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ClientFilesException(ClientFilesErrorCode.PermissionDenied, entry, ex.Message, ex);
            }

            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                try
                {
                    if ((attrs & FileAttributes.Directory) != 0)
                        Directory.Delete(entry);
                    else
                        File.Delete(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new ClientFilesException(ClientFilesErrorCode.PermissionDenied, entry, ex.Message, ex);
                }

                continue;
            }

            if ((attrs & FileAttributes.Directory) != 0)
            {
                DeleteTreeWithoutFollowingEscapingLinks(canonicalRoot, entry);
                continue;
            }

            try
            {
                File.Delete(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ClientFilesException(ClientFilesErrorCode.PermissionDenied, entry, ex.Message, ex);
            }
        }

        try
        {
            Directory.Delete(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ClientFilesException(ClientFilesErrorCode.PermissionDenied, directory, ex.Message, ex);
        }
    }

    private static void EnsureListedPathStaysUnderClient(string canonicalRoot, string path)
    {
        var fullRoot = ClientPaths.TrimTrailingSeparators(Path.GetFullPath(canonicalRoot));
        var fullPath = ClientPaths.TrimTrailingSeparators(Path.GetFullPath(path));
        if (!ClientPaths.IsPathUnderRoot(fullRoot, fullPath))
            throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, path,
                "Cache clear refused: path is outside the client directory.");
    }
}
