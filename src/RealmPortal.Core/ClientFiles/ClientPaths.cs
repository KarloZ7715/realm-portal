using System.Text.RegularExpressions;

namespace RealmPortal.Core.ClientFiles;

/// <summary>Canonical client-root paths and allow-list checks for apply/undo.</summary>
public static class ClientPaths
{
    private static readonly Regex LocaleFolder = new("^[a-z]{2}[A-Z]{2}\\z", RegexOptions.CultureInvariant);

    /// <summary>Returns the full path of an existing client directory, trimming trailing separators while preserving filesystem roots.</summary>
    /// <exception cref="ClientFilesException"><see cref="ClientFilesErrorCode.PathNotFound"/> when the directory does not exist.</exception>
    public static string CanonicalRoot(string clientRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientRoot);
        var full = Path.GetFullPath(clientRoot);
        if (!Directory.Exists(full))
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, full,
                $"The client directory '{full}' does not exist.");
        return TrimTrailingSeparators(full);
    }

    /// <summary>Checks that the manifest root, after canonicalization, matches the trusted canonical client root.</summary>
    /// <param name="trustedRoot">The caller's client directory, normalized by <see cref="CanonicalRoot"/>.</param>
    /// <param name="manifestRoot">The client directory recorded in the backup manifest.</param>
    /// <param name="manifestPath">The manifest path to report if the roots differ.</param>
    /// <exception cref="ClientFilesException"><see cref="ClientFilesErrorCode.VerifyFailed"/> when the roots differ.</exception>
    public static void RequireMatchingRoots(string trustedRoot, string manifestRoot, string manifestPath)
    {
        var manifestCanonical = TrimTrailingSeparators(Path.GetFullPath(manifestRoot));
        if (!RootsEqual(trustedRoot, manifestCanonical))
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, manifestPath,
                "The backup manifest client_root does not match the installation's client directory.");
    }

    /// <summary>Validates a managed relative path and returns its full path within the canonical client root.</summary>
    /// <param name="canonicalRoot">The client directory returned by <see cref="CanonicalRoot"/>.</param>
    /// <param name="relativePath">A '/'-separated path accepted by <see cref="ValidateManagedRelativePath"/>.</param>
    /// <exception cref="ClientFilesException">The path is malformed, unmanaged, or resolves outside the client directory.</exception>
    public static string ResolveManagedPath(string canonicalRoot, string relativePath)
    {
        ValidateManagedRelativePath(relativePath);
        return ResolveInside(canonicalRoot, relativePath);
    }

    /// <summary>Accepts only realmlist.wtf, Data/xxYY/realmlist.wtf locale paths, and WTF/Config.wtf.</summary>
    /// <remarks>File names and Data/WTF are case-insensitive; locales require two lowercase letters followed by two uppercase letters.</remarks>
    /// <exception cref="ClientFilesException">
    /// <see cref="ClientFilesErrorCode.PathNotFound"/> for malformed segments;
    /// <see cref="ClientFilesErrorCode.PathEscapesClient"/> for paths outside the managed set.
    /// </exception>
    public static void ValidateManagedRelativePath(string relative)
    {
        var segments = relative.Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || s.Contains('\\') || s.Contains(':')))
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, relative,
                $"'{relative}' is not a relative path inside the client folder.");

        if (segments.Length == 1 && string.Equals(segments[0], "realmlist.wtf", StringComparison.OrdinalIgnoreCase))
            return;

        if (segments.Length == 3
            && string.Equals(segments[0], "Data", StringComparison.OrdinalIgnoreCase)
            && LocaleFolder.IsMatch(segments[1])
            && string.Equals(segments[2], "realmlist.wtf", StringComparison.OrdinalIgnoreCase))
            return;

        if (segments.Length == 2
            && string.Equals(segments[0], "WTF", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "Config.wtf", StringComparison.OrdinalIgnoreCase))
            return;

        throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, relative,
            $"'{relative}' is not a Realm Portal managed client file path.");
    }

    /// <summary>Resolves a validated relative path against an absolute client root and rejects lexical escapes.</summary>
    private static string ResolveInside(string root, string relative)
    {
        var segments = relative.Split('/');
        string combined;
        try
        {
            combined = Path.GetFullPath(Path.Combine([root, .. segments]), root);
        }
        catch (ArgumentException ex)
        {
            throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, relative,
                $"'{relative}' resolves outside the client directory.", ex);
        }

        if (!IsUnderRoot(root, combined))
            throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, combined,
                $"'{relative}' resolves outside the client directory.");
        return combined;
    }

    /// <summary>Checks that a path relative to the root has no leading parent-directory component.</summary>
    private static bool IsUnderRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>Compares canonical roots ordinally, ignoring case on Windows.</summary>
    private static bool RootsEqual(string a, string b) =>
        OperatingSystem.IsWindows()
            ? string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            : string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>Removes trailing directory separators without shortening the filesystem root.</summary>
    private static string TrimTrailingSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        var end = path.Length;
        while (end > 0 && IsDirectorySeparator(path[end - 1]))
            end--;

        var rootLength = Path.GetPathRoot(path)?.Length ?? 0;
        if (end < rootLength)
            end = rootLength;

        return path[..end];
    }

    /// <summary>Reports whether a character is one of the platform's directory separators.</summary>
    private static bool IsDirectorySeparator(char c) =>
        c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
}
