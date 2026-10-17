using System.Text.RegularExpressions;

namespace RealmPortal.Core.ClientFiles;

/// <summary>Canonical client-root paths and allow-list checks for apply/undo.</summary>
public static class ClientPaths
{
    private static readonly Regex LocaleFolder = new("^[a-z]{2}[A-Z]{2}\\z", RegexOptions.CultureInvariant);

    public static string CanonicalRoot(string clientRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientRoot);
        var full = Path.GetFullPath(clientRoot);
        if (!Directory.Exists(full))
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, full,
                $"The client directory '{full}' does not exist.");
        return TrimTrailingSeparators(full);
    }

    public static void RequireMatchingRoots(string trustedRoot, string manifestRoot, string manifestPath)
    {
        var manifestCanonical = TrimTrailingSeparators(Path.GetFullPath(manifestRoot));
        if (!RootsEqual(trustedRoot, manifestCanonical))
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, manifestPath,
                "The backup manifest client_root does not match the installation's client directory.");
    }

    public static string ResolveManagedPath(string canonicalRoot, string relativePath)
    {
        ValidateManagedRelativePath(relativePath);
        var segments = relativePath.Split('/');
        var (realSegments, _) = CaseInsensitivePath.Resolve(canonicalRoot, segments, rejectAmbiguousMatches: true);
        var normalized = string.Join('/', realSegments);
        return ResolveInside(canonicalRoot, normalized);
    }

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

    private static bool IsUnderRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool RootsEqual(string a, string b) =>
        OperatingSystem.IsWindows()
            ? string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            : string.Equals(a, b, StringComparison.Ordinal);

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

    private static bool IsDirectorySeparator(char c) =>
        c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
}
