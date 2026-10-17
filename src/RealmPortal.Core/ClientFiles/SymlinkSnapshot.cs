using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RealmPortal.Core.ClientFiles;

/// <summary>Whether a client path was a symbolic link before apply, and the link text to recreate it.</summary>
internal readonly record struct SymlinkSnapshot(bool IsSymlink, string? Target)
{
    /// <summary>Symlink follows counted while deciding whether a backed-up link may be recreated.</summary>
    internal const int MaxLinkHops = 40;

    /// <summary>A sidecar larger than this is refused. Link text is a path, not a file payload.</summary>
    internal const int MaxSidecarBytes = 4096;

    private const int O_RDONLY = 0;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static SymlinkSnapshot? Capture(string path)
    {
        if (!File.Exists(path))
            return null;

        var linkText = new FileInfo(path).LinkTarget;
        return linkText is null ? null : new SymlinkSnapshot(true, linkText);
    }

    /// <summary>
    /// True when any directory entry exists at <paramref name="path"/>, including a symlink (dangling or not).
    /// An unreadable path counts as present so a sidecar is checked instead of skipped.
    /// </summary>
    public static bool NodeExists(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.LinkTarget != null || info.Exists || Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>
    /// Reads a symlink sidecar without following a link. The path must be a regular file (not a symlink or directory),
    /// at most <see cref="MaxSidecarBytes"/> bytes, valid UTF-8, non-empty, and free of NUL and other control characters.
    /// </summary>
    public static (byte[] Bytes, string Text) ReadSidecar(string path)
    {
        if (SidecarLooksUnsafe(path))
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                "The symlink sidecar is not a regular file.");

        byte[] bytes;
        try
        {
            bytes = ReadSidecarBytes(path);
        }
        catch (ClientFilesException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ClientFilesIo.Map(ex, path, "read symlink sidecar");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                "The symlink sidecar is not valid UTF-8.", ex);
        }

        if (text.Length == 0)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                "The symlink sidecar is empty.");
        foreach (var c in text)
        {
            if (c == '\0' || char.IsControl(c))
                throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                    "The symlink sidecar contains a control character.");
        }

        return (bytes, text);
    }

    /// <summary>
    /// True when <paramref name="linkText"/>, followed one hop at a time from <paramref name="linkPath"/>,
    /// stays inside <paramref name="clientRoot"/> and ends at an existing regular file there.
    /// An absolute or relative target outside the client, a dangling link, or more than <see cref="MaxLinkHops"/>
    /// hops returns false. This never throws for an escaping target.
    /// </summary>
    public static bool ChainStaysInsideClient(string clientRoot, string linkPath, string linkText)
    {
        try
        {
            return ChainStaysInsideClientCore(clientRoot, linkPath, linkText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public void Restore(string path)
    {
        if (!IsSymlink || string.IsNullOrEmpty(Target))
            return;
        // Delete only the path we are about to replace. File.Delete on a symlink unlinks that symlink.
        var info = new FileInfo(path);
        if (info.LinkTarget != null || info.Exists)
            File.Delete(path);
        File.CreateSymbolicLink(path, Target);
    }

    /// <summary>Removes a symlink at <paramref name="path"/> and never its target. A regular file is left alone.</summary>
    public static void DeleteLinkOnly(string path)
    {
        try
        {
            if (new FileInfo(path).LinkTarget is null)
                return;
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool ChainStaysInsideClientCore(string clientRoot, string linkPath, string linkText)
    {
        if (string.IsNullOrEmpty(linkText) || string.IsNullOrEmpty(linkPath))
            return false;

        var root = TrimTrailing(Path.GetFullPath(clientRoot));
        var parent = Path.GetDirectoryName(linkPath);
        if (string.IsNullOrEmpty(parent))
            return false;

        var hops = 0;
        // Parent directories of the managed path are resolved inside the client. Symlinks above the client root
        // are not followed: the canonical root string is the boundary.
        if (!TryResolveInside(root, parent, ref hops, out var resolvedParent))
            return false;
        if (new FileInfo(resolvedParent).LinkTarget != null || !Directory.Exists(resolvedParent))
            return false;

        // The link we would recreate is itself one hop.
        if (++hops > MaxLinkHops)
            return false;

        bool landedOk;
        string landed;
        if (Path.IsPathRooted(linkText))
            landedOk = TryWalkAbsoluteInside(root, linkText, ref hops, out landed);
        else
            landedOk = TryWalk(root, resolvedParent, SplitRelative(linkText), ref hops, out landed);

        return landedOk && IsRegularFile(landed) && IsInside(root, landed);
    }

    private static bool TryResolveInside(string root, string absolutePath, ref int hops, out string resolved)
    {
        resolved = root;
        if (PathsEqual(root, absolutePath))
            return true;
        if (!IsInside(root, absolutePath))
            return false;
        return TryWalk(root, root, SplitRelative(Path.GetRelativePath(root, absolutePath)), ref hops, out resolved);
    }

    /// <summary>
    /// An absolute path is eligible only when its prefix matches the client root lexically. The remainder is then
    /// walked inside the root, so a symlink under the client cannot escape and a path outside is rejected before it is opened.
    /// </summary>
    private static bool TryWalkAbsoluteInside(string root, string absolute, ref int hops, out string resolved)
    {
        resolved = "";
        if (!SamePathRoot(root, absolute))
            return false;

        var rootParts = Components(root);
        var parts = Components(absolute);
        if (parts.Count < rootParts.Count)
            return false;
        for (var i = 0; i < rootParts.Count; i++)
        {
            if (!SegmentEqual(rootParts[i], parts[i]))
                return false;
        }

        var inside = parts.GetRange(rootParts.Count, parts.Count - rootParts.Count);
        return TryWalk(root, root, inside, ref hops, out resolved);
    }

    private static bool TryWalk(string root, string start, List<string> segments, ref int hops, out string resolved)
    {
        resolved = "";
        if (!IsInsideOrEqual(root, start))
            return false;

        var current = start;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0 || segment == ".")
                continue;
            if (segment == "..")
            {
                if (PathsEqual(current, root))
                    return false;
                var parent = Path.GetDirectoryName(TrimTrailing(current));
                if (parent is null || !IsInsideOrEqual(root, parent))
                    return false;
                current = parent;
                continue;
            }

            if (segment.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(segment))
                return false;

            var next = Path.Combine(current, segment);
            if (!IsInside(root, next))
                return false;

            if (!TryIsSymlink(next, out var isLink))
                return false;
            if (!isLink)
            {
                current = next;
                continue;
            }

            if (!TryReadHop(next, ref hops, out var raw))
                return false;

            string landed;
            if (Path.IsPathRooted(raw))
            {
                if (!TryWalkAbsoluteInside(root, raw, ref hops, out landed))
                    return false;
            }
            else if (!TryWalk(root, current, SplitRelative(raw), ref hops, out landed))
            {
                return false;
            }

            if (!IsInsideOrEqual(root, landed))
                return false;
            current = landed;
        }

        if (!IsInsideOrEqual(root, current))
            return false;
        resolved = current;
        return true;
    }

    /// <summary>One hop: <see cref="File.ResolveLinkTarget"/> with <c>returnFinalTarget: false</c>, then the raw text relative to the link's parent.</summary>
    private static bool TryReadHop(string linkPath, ref int hops, out string rawTarget)
    {
        rawTarget = "";
        if (++hops > MaxLinkHops)
            return false;

        if (File.ResolveLinkTarget(linkPath, returnFinalTarget: false) is null)
            return false;
        var raw = new FileInfo(linkPath).LinkTarget;
        if (string.IsNullOrEmpty(raw))
            return false;
        rawTarget = raw;
        return true;
    }

    private static bool TryIsSymlink(string path, out bool isLink)
    {
        isLink = false;
        try
        {
            isLink = new FileInfo(path).LinkTarget != null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsRegularFile(string path) => TryStatRegular(path, out _);

    private static bool TryStatRegular(string path, out long size)
    {
        size = 0;
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget != null || !File.Exists(path) || Directory.Exists(path))
                return false;
            if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                return false;
            size = info.Length;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Link or directory. Errors refuse the sidecar: a path we cannot classify is not opened.</summary>
    private static bool SidecarLooksUnsafe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.LinkTarget != null || Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    private static byte[] ReadSidecarBytes(string path)
    {
        if (OperatingSystem.IsWindows())
            return ReadSidecarWindows(path);
        if (TryGetNoFollowOpenFlags(out var flags))
            return ReadSidecarNoFollow(path, flags);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadAtMost(stream, path, MaxSidecarBytes);
    }

    private static byte[] ReadSidecarWindows(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists
            || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                "The symlink sidecar is not a regular file.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadAtMost(stream, path, MaxSidecarBytes);
    }

    private static byte[] ReadSidecarNoFollow(string path, int flags)
    {
        var fd = open(path, flags);
        if (fd < 0)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                "The symlink sidecar is not a regular file or could not be opened.");

        using var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        using var stream = new FileStream(handle, FileAccess.Read);
        return ReadAtMost(stream, path, MaxSidecarBytes);
    }

    /// <summary>
    /// <c>O_NOFOLLOW</c> is not the same value on every CPU. An architecture without a known value does not call <c>open</c>.
    /// </summary>
    private static bool TryGetNoFollowOpenFlags(out int flags)
    {
        if (OperatingSystem.IsMacOS())
        {
            const int oNoFollow = 0x100;
            const int oNonBlock = 0x4;
            const int oCloExec = 0x1000000;
            flags = O_RDONLY | oNoFollow | oNonBlock | oCloExec;
            return true;
        }

        if (!OperatingSystem.IsLinux())
        {
            flags = 0;
            return false;
        }

        var noFollow = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 or Architecture.X86 => 0x20000,
            Architecture.Arm or Architecture.Arm64 => 0x8000,
            _ => 0,
        };
        if (noFollow == 0)
        {
            flags = 0;
            return false;
        }

        const int oNonBlockLinux = 0x800;
        const int oCloExecLinux = 0x80000;
        flags = O_RDONLY | noFollow | oNonBlockLinux | oCloExecLinux;
        return true;
    }

    private static byte[] ReadAtMost(Stream stream, string path, int maxBytes)
    {
        var buffer = new byte[maxBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
                break;
            read += n;
        }

        if (read > maxBytes)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                "The symlink sidecar is larger than 4096 bytes.");

        var bytes = new byte[read];
        buffer.AsSpan(0, read).CopyTo(bytes);
        return bytes;
    }

    private static List<string> Components(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        var rest = root.Length <= path.Length ? path[root.Length..] : "";
        return SplitRelative(rest);
    }

    private static List<string> SplitRelative(string path)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i <= path.Length; i++)
        {
            if (i == path.Length || path[i] == Path.DirectorySeparatorChar || path[i] == Path.AltDirectorySeparatorChar)
            {
                if (i > start)
                    parts.Add(path[start..i]);
                start = i + 1;
            }
        }

        return parts;
    }

    private static bool SamePathRoot(string a, string b)
    {
        var left = Path.GetPathRoot(a);
        var right = Path.GetPathRoot(b);
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;
        return string.Equals(left, right, PathComparison);
    }

    private static bool SegmentEqual(string a, string b) => string.Equals(a, b, PathComparison);

    private static bool IsInsideOrEqual(string root, string path) =>
        PathsEqual(root, path) || IsInside(root, path);

    private static bool IsInside(string root, string path)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(root, path);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (relative is "." or "..")
            return false;
        if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            return false;
        return !Path.IsPathRooted(relative);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(TrimTrailing(a), TrimTrailing(b), PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string TrimTrailing(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        var end = path.Length;
        while (end > 0 && (path[end - 1] == Path.DirectorySeparatorChar || path[end - 1] == Path.AltDirectorySeparatorChar))
            end--;

        var rootLength = Path.GetPathRoot(path)?.Length ?? 0;
        if (end < rootLength)
            end = rootLength;
        return path[..end];
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
}
