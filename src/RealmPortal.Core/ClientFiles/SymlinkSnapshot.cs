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

    /// <summary><c>ENOENT</c>. <see cref="Marshal.GetLastPInvokeError"/> returns this when <c>open</c> fails because the path is missing.</summary>
    private const int ENOENT = 2;

    private const int EPERM = 1;
    private const int EACCES = 13;

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

    /// <summary>
    /// Opens <paramref name="path"/> once for reading and does not follow the final symlink.
    /// On Linux and macOS this is <c>open</c> with <c>O_RDONLY|O_NOFOLLOW|O_NONBLOCK|O_CLOEXEC</c>.
    /// A missing path throws <see cref="FileNotFoundException"/> so the caller can map it to
    /// <see cref="ClientFilesErrorCode.PathNotFound"/>. Any other failed open is
    /// <see cref="ClientFilesErrorCode.VerifyFailed"/>.
    /// On Windows the reparse-point attributes are checked and then a <see cref="FileStream"/> is opened.
    /// Those are two steps: a symlink swapped in between is followed. That race remains because this path
    /// has no <c>O_NOFOLLOW</c>.
    /// </summary>
    internal static FileStream OpenNoFollowForRead(string path)
    {
        if (OperatingSystem.IsWindows())
            return OpenWindowsNoFollow(path);

        if (!TryGetNoFollowOpenFlags(out var flags))
        {
            // A following open would trust a symlink. Linux without a known O_NOFOLLOW value refuses instead.
            if (OperatingSystem.IsLinux())
                throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                    "The file cannot be opened without following links on this system.");

            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        return OpenUnix(path, flags, followFinalLink: false);
    }

    /// <summary>
    /// Opens <paramref name="path"/> for reading and follows a final symlink. A managed client path may be a link.
    /// On Unix the open is <c>O_RDONLY|O_NONBLOCK|O_CLOEXEC</c> (no <c>O_NOFOLLOW</c>), so a FIFO does not block
    /// waiting for a writer. The caller must still require a seekable regular file and a size cap: <c>O_NONBLOCK</c>
    /// only stops the open from hanging. On Windows this is an ordinary <see cref="FileStream"/>.
    /// A missing path throws <see cref="FileNotFoundException"/>.
    /// </summary>
    internal static FileStream OpenFollowNonBlockingForRead(string path)
    {
        if (!TryGetNonBlockingFollowFlags(out var flags))
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        return OpenUnix(path, flags, followFinalLink: true);
    }

    /// <summary>
    /// True when <paramref name="stream"/> is a seekable regular file. <paramref name="length"/> is that stream's length.
    /// A FIFO is not seekable. On Linux some character devices still report <see cref="Stream.CanSeek"/> and a length of
    /// zero, so the opened descriptor (not the path) is also checked with <c>statx</c>.
    /// </summary>
    internal static bool IsSeekableRegularFile(FileStream stream, out long length)
    {
        length = 0;
        if (!stream.CanSeek)
            return false;
        if (OperatingSystem.IsLinux() && !LinuxFdIsRegularOrUnknown(stream.SafeFileHandle))
            return false;

        try
        {
            length = stream.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }

        return length >= 0;
    }

    private static byte[] ReadSidecarBytes(string path)
    {
        using var stream = OpenNoFollowForRead(path);
        return ReadAtMost(stream, path, MaxSidecarBytes);
    }

    /// <summary>
    /// Residual race on Windows: <see cref="FileInfo.LinkTarget"/> and the reparse-point attributes are read,
    /// then <see cref="FileStream"/> opens <paramref name="path"/> by name. A symlink swapped in between is followed.
    /// </summary>
    private static FileStream OpenWindowsNoFollow(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget != null)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                "The file is not a regular file or could not be opened.");

        // Attributes throws FileNotFoundException when the path is missing. The caller maps that to PathNotFound.
        if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                "The file is not a regular file or could not be opened.");

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static FileStream OpenUnix(string path, int flags, bool followFinalLink)
    {
        int fd;
        try
        {
            fd = open(path, flags);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            if (!followFinalLink)
                throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                    "The file could not be opened without following links.", ex);
            throw new IOException($"The file '{path}' could not be opened without blocking.", ex);
        }

        if (fd < 0)
        {
            // GetLastPInvokeError must run before any other P/Invoke. 2 is ENOENT.
            var errno = Marshal.GetLastPInvokeError();
            if (errno == ENOENT)
                throw new FileNotFoundException($"Could not find file '{path}'.", path);

            if (!followFinalLink)
                throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                    "The file is not a regular file or could not be opened.");

            if (errno is EPERM or EACCES)
                throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");
            throw new IOException($"Could not open file '{path}' (errno {errno}).");
        }

        var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        try
        {
            return new FileStream(handle, FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// <c>statx</c> on the descriptor already opened. True when it is a regular file, or when the type could not be read
    /// (the caller still requires <see cref="Stream.CanSeek"/>). False when <c>statx</c> reports another file type.
    /// </summary>
    private static bool LinuxFdIsRegularOrUnknown(SafeFileHandle handle)
    {
        const int atEmptyPath = 0x1000;
        const uint statxType = 1;
        const int modeOffset = 28;
        const ushort ifmt = 0xF000;
        const ushort ifreg = 0x8000;

        var buffer = new byte[256];
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            var fd = (int)handle.DangerousGetHandle();
            var rc = statx(fd, string.Empty, atEmptyPath, statxType, buffer);
            if (rc < 0)
                return true;

            var mask = BitConverter.ToUInt32(buffer, 0);
            if ((mask & statxType) == 0)
                return true;

            var mode = BitConverter.ToUInt16(buffer, modeOffset);
            return (mode & ifmt) == ifreg;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
        finally
        {
            if (added)
                handle.DangerousRelease();
        }
    }

    /// <summary>
    /// <c>O_RDONLY|O_NONBLOCK|O_CLOEXEC</c> without <c>O_NOFOLLOW</c>. The non-blocking and close-on-exec bits match
    /// <see cref="TryGetNoFollowOpenFlags"/> and do not depend on the CPU, so an unknown Linux architecture still follows.
    /// </summary>
    private static bool TryGetNonBlockingFollowFlags(out int flags)
    {
        if (OperatingSystem.IsMacOS())
        {
            const int oNonBlock = 0x4;
            const int oCloExec = 0x1000000;
            flags = O_RDONLY | oNonBlock | oCloExec;
            return true;
        }

        if (OperatingSystem.IsLinux())
        {
            const int oNonBlockLinux = 0x800;
            const int oCloExecLinux = 0x80000;
            flags = O_RDONLY | oNonBlockLinux | oCloExecLinux;
            return true;
        }

        flags = 0;
        return false;
    }

    /// <summary>
    /// <c>O_NOFOLLOW</c> is not the same value on every CPU. Linux without a known value returns false;
    /// the caller refuses the sidecar instead of following it.
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

    private static byte[] ReadAtMost(FileStream stream, string path, int maxBytes)
    {
        // Same descriptor OpenNoFollowForRead returned. A FIFO is not seekable; statx rejects a character device
        // that still reports CanSeek. Reject either before a read, which can block or return attacker bytes.
        if (!IsSeekableRegularFile(stream, out _))
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                "The symlink sidecar is not a regular file.");

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

    [DllImport("libc", SetLastError = true)]
    private static extern int statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags, uint mask, byte[] buffer);
}
