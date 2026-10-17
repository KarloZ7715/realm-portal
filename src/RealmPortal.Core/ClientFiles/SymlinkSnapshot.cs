using System.Text;

namespace RealmPortal.Core.ClientFiles;

/// <summary>Whether a client path was a symbolic link before apply, and the link text to recreate it.</summary>
internal readonly record struct SymlinkSnapshot(bool IsSymlink, string? Target)
{
    public static SymlinkSnapshot? Capture(string path)
    {
        if (!File.Exists(path))
            return null;

        var linkText = new FileInfo(path).LinkTarget;
        return linkText is null ? null : new SymlinkSnapshot(true, linkText);
    }

    public static string ReadValidatedSidecar(string clientRoot, string linkPath, byte[] sidecarBytes)
    {
        var linkText = Encoding.UTF8.GetString(sidecarBytes);
        if (string.IsNullOrEmpty(linkText))
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, linkPath,
                "The backup symlink sidecar is empty.");
        EnsureTargetResolvesInsideClient(clientRoot, linkPath, linkText);
        return linkText;
    }

    public void Restore(string path)
    {
        if (!IsSymlink || string.IsNullOrEmpty(Target))
            return;
        if (File.Exists(path))
            File.Delete(path);
        File.CreateSymbolicLink(path, Target);
    }

    internal static void EnsureTargetResolvesInsideClient(string clientRoot, string linkPath, string linkText)
    {
        var baseDir = Path.GetDirectoryName(linkPath)
            ?? throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, linkPath,
                "Cannot resolve a symlink without a parent directory.");

        string resolved;
        try
        {
            resolved = Path.IsPathRooted(linkText)
                ? Path.GetFullPath(linkText)
                : Path.GetFullPath(Path.Combine(baseDir, linkText));
        }
        catch (ArgumentException ex)
        {
            throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, linkPath,
                "The symlink target does not resolve inside the client directory.", ex);
        }

        if (!IsUnderRoot(clientRoot, resolved))
            throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, linkPath,
                "The symlink target resolves outside the client directory.");
    }

    private static bool IsUnderRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
