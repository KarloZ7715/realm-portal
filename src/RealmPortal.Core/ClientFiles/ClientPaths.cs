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
            throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, manifestPath,
                $"The backup to undo belongs to a different client folder ('{manifestCanonical}'), not '{trustedRoot}'; undo stopped before changing any file.");
    }

    public static string ResolveManagedPath(string canonicalRoot, string relativePath)
    {
        ValidateManagedRelativePath(relativePath);
        var combined = ResolveInside(canonicalRoot, relativePath);
        RequireParentInsideThroughLinks(canonicalRoot, relativePath, combined);
        return combined;
    }

    private const int MaxLinkHops = 40;

    /// <summary>
    /// <see cref="Path.GetFullPath(string)"/> does not follow symbolic links, so a managed folder (<c>Data</c>, <c>Data/enUS</c>,
    /// <c>WTF</c>) that is a link or junction to a folder outside the client would let a write or delete land outside it.
    /// Resolves the parent folder of the managed file one link hop at a time and refuses it unless it stays under the
    /// (resolved) client root. The leaf file itself is not followed: writes replace it atomically.
    /// </summary>
    private static void RequireParentInsideThroughLinks(string canonicalRoot, string relative, string combined)
    {
        var parent = Path.GetDirectoryName(combined);
        if (parent is null)
            return;

        var hops = 0;
        string realRoot, realParent;
        try
        {
            realRoot = ResolveLinks(canonicalRoot, ref hops);
            hops = 0;
            realParent = ResolveLinks(parent, ref hops);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, relative,
                $"'{relative}' could not be resolved inside the client directory: {ex.Message}", ex);
        }

        if (!IsUnderRoot(realRoot, realParent))
            throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, relative,
                $"'{relative}' resolves outside the client directory through a symbolic link.");
    }

    /// <summary>Returns <paramref name="path"/> with every existing link component replaced by its target, one hop at a time.</summary>
    private static string ResolveLinks(string path, ref int hops)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var pending = new Stack<string>(full[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Reverse());
        var current = root;
        var exists = true;
        while (pending.Count > 0)
        {
            var segment = pending.Pop();
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            var next = Path.Combine(current, segment);
            if (exists)
            {
                if (new DirectoryInfo(next).LinkTarget is { } target)
                {
                    if (++hops > MaxLinkHops)
                        throw new IOException($"Too many symbolic links while resolving '{path}'.");

                    // Push the raw target segments so '..' inside the target is applied to the already resolved folder.
                    if (Path.IsPathRooted(target))
                        current = Path.GetPathRoot(target) ?? current;
                    var targetRoot = Path.IsPathRooted(target) ? Path.GetPathRoot(target)?.Length ?? 0 : 0;
                    foreach (var part in target[targetRoot..]
                                 .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                                 .Reverse())
                        pending.Push(part);
                    continue;
                }

                exists = Directory.Exists(next) || File.Exists(next);
            }

            current = next;
        }

        return TrimTrailingSeparators(current);
    }

    public static void ValidateManagedRelativePath(string relative)
    {
        var segments = relative.Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || s.Contains('\\') || s.Contains(':')))
            throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, relative,
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

    /// <summary>
    /// Drops trailing directory separators while keeping the filesystem root (<c>/</c> or <c>C:\</c>).
    /// <see cref="CanonicalRoot"/> and client-id derivation both use this form.
    /// </summary>
    internal static string TrimTrailingSeparators(string path)
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
