using System.Runtime.InteropServices;

namespace RealmPortal.Core.Updates;

/// <summary>Passwd entry used by the private-group check and its test seam (#206).</summary>
public sealed record UnixPasswd(string Name, uint Uid, uint Gid);

/// <summary>Group entry used by the private-group check and its test seam (#206).</summary>
public sealed record UnixGroup(string Name, uint Gid, IReadOnlyList<string> Members);

/// <summary>Result of <c>lstat</c>/<c>statx</c> without following a final symlink.</summary>
public readonly record struct UnixLstat(
    ulong Device,
    ulong Inode,
    int RawMode,
    uint Uid,
    uint Gid)
{
    public const int FileTypeMask = 0xF000;
    public const int Socket = 0xC000;
    public const int SymbolicLink = 0xA000;
    public const int RegularFile = 0x8000;
    public const int BlockDevice = 0x6000;
    public const int Directory = 0x4000;
    public const int CharacterDevice = 0x2000;
    public const int Fifo = 0x1000;
    public const int PermissionMask = 0x0FFF;
    public const int SetUid = 0b100_000_000_000;
    public const int SetGid = 0b010_000_000_000;
    public const int Sticky = 0b001_000_000_000;
    public const int GroupWrite = 0b000_010_010 & 0b000_010_000;
    public const int OtherWrite = 0b000_000_010;
    public const int GroupOrOtherWrite = GroupWrite | OtherWrite;

    public int Permissions => RawMode & PermissionMask;
    public bool IsRegularFile => (RawMode & FileTypeMask) == RegularFile;
    public bool IsDirectory => (RawMode & FileTypeMask) == Directory;
    public bool IsSymlink => (RawMode & FileTypeMask) == SymbolicLink;
    public bool OwnedByUser => Uid == UnixPathSafety.EffectiveUserId;
    public bool OwnedByRoot => Uid == 0;
}

/// <summary>
/// Path permission checks and directory-relative file operations for the update installer (#206).
/// Unix only; Windows callers treat destinations as safe and use <see cref="File.Move"/>.
/// </summary>
public static class UnixPathSafety
{
    public const int ErrnoExist = 17;
    public const int ErrnoExdev = 18;

    /// <summary>Test seam for passwd lookups. Production leaves this null.</summary>
    internal static Func<uint, UnixPasswd?>? LookupPasswdForTests { get; set; }

    /// <summary>Test seam for group lookups. Production leaves this null.</summary>
    internal static Func<uint, UnixGroup?>? LookupGroupForTests { get; set; }

    /// <summary>
    /// Test seam for <c>getpwent</c>. A null delegate uses the real database; a delegate that returns
    /// <c>null</c> means enumeration is not possible (rules 1–3 suffice).
    /// </summary>
    internal static Func<IReadOnlyList<UnixPasswd>?>? EnumeratePasswdForTests { get; set; }

    /// <summary>Test seam: throw <see cref="EntryPointNotFoundException"/> from <c>renameat2</c>.</summary>
    internal static bool SimulateMissingRenameAt2 { get; set; }

    public static uint EffectiveUserId => OperatingSystem.IsWindows() ? uint.MaxValue : Geteuid();

    public static bool TryLstat(string path, out UnixLstat info)
    {
        info = default;
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(path))
            return false;
        return Native.TryStatx(path, out info);
    }

    /// <summary><c>realpath(3)</c>; null when the path cannot be resolved.</summary>
    public static string? RealPath(string path)
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(path))
            return null;
        var ptr = Native.realpath(path, IntPtr.Zero);
        if (ptr == IntPtr.Zero)
            return null;
        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            Native.free(ptr);
        }
    }

    /// <summary>
    /// The directory is owned by the current user, nobody else can write it (see
    /// <see cref="IsOwnedNodeSafe"/>), and every ancestor up to <c>/</c> is owned by the user or root
    /// and is not group- or other-writable unless the sticky bit is set. Root-owned ancestors may
    /// have group-write only with sticky. User-owned directories may have group-write without sticky
    /// only when the group is a strict private group.
    /// </summary>
    public static bool IsDirectoryTreeSafe(string directory, out string? reason)
    {
        reason = null;
        if (OperatingSystem.IsWindows())
            return true;
        if (string.IsNullOrEmpty(directory))
        {
            reason = "Path is empty.";
            return false;
        }

        var resolved = RealPath(directory);
        if (resolved is null)
        {
            reason = "Path cannot be resolved.";
            return false;
        }

        if (!TryLstat(resolved, out var leaf) || !leaf.IsDirectory)
        {
            reason = "Target directory is not a plain directory.";
            return false;
        }

        if (!leaf.OwnedByUser)
        {
            reason = "Target directory is not owned by the current user.";
            return false;
        }

        if (!IsOwnedNodeSafe(resolved, leaf, allowRootOwner: false, out reason))
            return false;

        var current = resolved;
        while (true)
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current)
                break;
            if (!TryLstat(parent, out var st) || !st.IsDirectory)
            {
                reason = "An ancestor is not a plain directory.";
                return false;
            }

            if (!st.OwnedByUser && !st.OwnedByRoot)
            {
                reason = "An ancestor is not owned by the current user or root.";
                return false;
            }

            if (!IsOwnedNodeSafe(parent, st, allowRootOwner: true, out reason))
                return false;

            current = parent;
        }

        return true;
    }

    /// <summary>
    /// A regular file owned by the current user that no other user can write: never other-writable,
    /// and group-writable only when the group is a strict private group.
    /// </summary>
    public static bool IsRegularFileSafe(string path, out string? reason)
    {
        reason = null;
        if (OperatingSystem.IsWindows())
            return true;
        if (!TryLstat(path, out var st) || !st.IsRegularFile || !st.OwnedByUser)
        {
            reason = "File is not a regular file owned by the current user.";
            return false;
        }

        return IsOwnedNodeSafe(path, st, allowRootOwner: false, out reason);
    }

    public static bool IsOwnedNodeSafe(string path, UnixLstat st, bool allowRootOwner, out string? reason)
    {
        reason = null;
        if (st.OwnedByRoot && allowRootOwner)
        {
            if ((st.Permissions & UnixLstat.GroupOrOtherWrite) != 0 && (st.Permissions & UnixLstat.Sticky) == 0)
            {
                reason = "A root-owned ancestor is writable by other users.";
                return false;
            }

            return true;
        }

        if (!st.OwnedByUser)
        {
            reason = "Path is not owned by the current user.";
            return false;
        }

        if ((st.Permissions & UnixLstat.OtherWrite) != 0 && (st.Permissions & UnixLstat.Sticky) == 0)
        {
            reason = "Path is writable by other users.";
            return false;
        }

        if ((st.Permissions & UnixLstat.GroupWrite) != 0 && (st.Permissions & UnixLstat.Sticky) == 0
            && !IsStrictPrivateGroup(st.Uid, st.Gid, path))
        {
            reason = "Path is group-writable without a private group.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// User-private group exception for umask 002: allow <c>g+w</c> never <c>o+w</c> only when every
    /// check holds. Any lookup or parse failure means the exception does not apply.
    /// </summary>
    public static bool IsStrictPrivateGroup(uint fileUid, uint fileGid, string path)
    {
        try
        {
            if (fileUid != EffectiveUserId)
                return false;
            // (1) st_uid == euid, st_gid == pw_gid(euid) and gid == uid (UPG numbering).
            if (fileGid != fileUid)
                return false;
            if (!TryGetPasswd(fileUid, out var passwd) || passwd.Gid != fileGid || passwd.Uid != fileUid)
                return false;
            // (2) group name equals the user name.
            if (!TryGetGroup(fileGid, out var group) || !string.Equals(group.Name, passwd.Name, StringComparison.Ordinal))
                return false;
            // (3) gr_mem empty or exactly [user].
            if (group.Members.Count > 1)
                return false;
            if (group.Members.Count == 1
                && !string.Equals(group.Members[0], passwd.Name, StringComparison.Ordinal))
                return false;
            // (4) no other passwd entry has this pw_gid, when enumeration is possible.
            if (TryEnumeratePasswd(out var entries))
            {
                foreach (var other in entries)
                {
                    if (other.Gid == fileGid && other.Uid != fileUid)
                        return false;
                }
            }

            // (5) no access ACL on this inode.
            if (HasPosixAccessAcl(path))
                return false;

            return true;
        }
        catch
        {
            // (6) any lookup/parse failure → strict mode (no g+w exception).
            return false;
        }
    }

    public static bool TryRenameNoReplace(string oldPath, string newPath, out int errno) =>
        TryRename(oldPath, newPath, overwrite: false, out errno);

    public static bool TryRenameOverwrite(string oldPath, string newPath, out int errno) =>
        TryRename(oldPath, newPath, overwrite: true, out errno);

    public static bool TryHardLink(string existingFile, string linkPath, out int errno)
    {
        errno = 0;
        if (OperatingSystem.IsWindows())
            return false;
        var oldDirPath = Path.GetDirectoryName(existingFile)!;
        var newDirPath = Path.GetDirectoryName(linkPath)!;
        if (!TryLstat(oldDirPath, out var oldSt) || !oldSt.IsDirectory
            || !TryLstat(newDirPath, out var newSt) || !newSt.IsDirectory)
        {
            errno = Native.ErrnoNoent;
            return false;
        }

        using var oldDir = DirFd.OpenMatching(oldDirPath, oldSt);
        using var newDir = DirFd.OpenMatching(newDirPath, newSt);
        if (oldDir.Fd < 0 || newDir.Fd < 0)
        {
            errno = Marshal.GetLastPInvokeError();
            return false;
        }

        if (Native.linkat(oldDir.Fd, Path.GetFileName(existingFile), newDir.Fd, Path.GetFileName(linkPath), 0) == 0)
            return true;
        errno = Marshal.GetLastPInvokeError();
        return false;
    }

    public static void FsyncDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
            return;
        if (!TryLstat(directory, out var st) || !st.IsDirectory)
            return;
        using var dir = DirFd.OpenMatching(directory, st, pathOnly: false);
        if (dir.Fd < 0)
            return;
        _ = Native.fsync(dir.Fd);
    }

    /// <summary>
    /// Deletes only regular files owned by the user matching <paramref name="searchPattern"/>,
    /// then removes the empty directory. Does nothing if <paramref name="directory"/> is not a
    /// user-owned real directory or its parent is not safe.
    /// </summary>
    public static bool TryDeleteOwnedFilesThenRmdir(string directory, string searchPattern)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var file in Directory.EnumerateFiles(directory, searchPattern))
                    File.Delete(file);
                Directory.Delete(directory, recursive: false);
                return true;
            }

            if (!TryLstat(directory, out var st) || !st.IsDirectory || !st.OwnedByUser)
                return false;

            // Open with O_NOFOLLOW so a symlink swap is refused. Names are unlinked relative to that
            // descriptor, not by walking a path that could have been replaced.
            using (var dir = DirFd.OpenMatching(directory, st, pathOnly: false))
            {
                if (dir.Fd < 0)
                    return false;
                foreach (var file in Directory.EnumerateFiles(directory, searchPattern))
                {
                    var name = Path.GetFileName(file);
                    if (!Native.TryStatxAt(dir.Fd, name, out var fs) || !fs.IsRegularFile || !fs.OwnedByUser)
                        continue;
                    _ = Native.unlinkat(dir.Fd, name, 0);
                }
            }

            Directory.Delete(directory, recursive: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRename(string oldPath, string newPath, bool overwrite, out int errno)
    {
        errno = 0;
        if (OperatingSystem.IsWindows())
            return false;
        var oldDirPath = Path.GetDirectoryName(oldPath)!;
        var newDirPath = Path.GetDirectoryName(newPath)!;
        if (!TryLstat(oldDirPath, out var oldSt) || !oldSt.IsDirectory
            || !TryLstat(newDirPath, out var newSt) || !newSt.IsDirectory)
        {
            errno = Native.ErrnoNoent;
            return false;
        }

        using var oldDir = DirFd.OpenMatching(oldDirPath, oldSt);
        using var newDir = DirFd.OpenMatching(newDirPath, newSt);
        if (oldDir.Fd < 0 || newDir.Fd < 0)
        {
            errno = Marshal.GetLastPInvokeError();
            return false;
        }

        var oldName = Path.GetFileName(oldPath);
        var newName = Path.GetFileName(newPath);
        var flags = overwrite ? 0u : Native.RenameNoreplace;
        if (TryRenameAt2(oldDir.Fd, oldName, newDir.Fd, newName, flags, out errno))
            return true;

        if (errno != Native.ErrnoNosys)
            return false;

        if (overwrite)
        {
            // renameat2 missing: renameat with flags 0, never a copy (a copy would truncate a hard-linked .bak).
            if (Native.renameat(oldDir.Fd, oldName, newDir.Fd, newName) == 0)
                return true;
            errno = Marshal.GetLastPInvokeError();
            return false;
        }

        // renameat2 missing: link then unlink, which fails on EEXIST instead of replacing.
        if (Native.linkat(oldDir.Fd, oldName, newDir.Fd, newName, 0) != 0)
        {
            errno = Marshal.GetLastPInvokeError();
            return false;
        }

        if (Native.unlinkat(oldDir.Fd, oldName, 0) != 0)
        {
            errno = Marshal.GetLastPInvokeError();
            _ = Native.unlinkat(newDir.Fd, newName, 0);
            return false;
        }

        return true;
    }

    private static bool TryRenameAt2(int olddirfd, string oldName, int newdirfd, string newName, uint flags, out int errno)
    {
        try
        {
            if (SimulateMissingRenameAt2)
                throw new EntryPointNotFoundException("renameat2");
            if (Native.renameat2(olddirfd, oldName, newdirfd, newName, flags) == 0)
            {
                errno = 0;
                return true;
            }

            errno = Marshal.GetLastPInvokeError();
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            errno = Native.ErrnoNosys;
            return false;
        }
    }

    private static bool TryGetPasswd(uint uid, out UnixPasswd passwd)
    {
        if (LookupPasswdForTests is { } hook)
        {
            var mapped = hook(uid);
            if (mapped is null)
            {
                passwd = null!;
                return false;
            }

            passwd = mapped;
            return true;
        }

        return Native.TryGetPasswd(uid, out passwd);
    }

    private static bool TryGetGroup(uint gid, out UnixGroup group)
    {
        if (LookupGroupForTests is { } hook)
        {
            var mapped = hook(gid);
            if (mapped is null)
            {
                group = null!;
                return false;
            }

            group = mapped;
            return true;
        }

        return Native.TryGetGroup(gid, out group);
    }

    private static bool TryEnumeratePasswd(out IReadOnlyList<UnixPasswd> entries)
    {
        if (EnumeratePasswdForTests is { } hook)
        {
            var mapped = hook();
            if (mapped is null)
            {
                entries = Array.Empty<UnixPasswd>();
                return false;
            }

            entries = mapped;
            return true;
        }

        return Native.TryEnumeratePasswd(out entries);
    }

    private static bool HasPosixAccessAcl(string path)
    {
        var size = Native.lgetxattr(path, Native.PosixAclAccess, IntPtr.Zero, 0);
        if (size >= 0)
            return true;
        var err = Marshal.GetLastPInvokeError();
        // ENODATA: no such attribute. ENOTSUP/EOPNOTSUPP: the filesystem has no xattrs, so no ACL.
        return err is not (Native.ErrnoNodata or Native.ErrnoNotsup);
    }

    private sealed class DirFd : IDisposable
    {
        public int Fd { get; }

        private DirFd(int fd) => Fd = fd;

        public static DirFd Open(string path, bool pathOnly = true)
        {
            var flags = Native.ODirectory | Native.ONofollow | Native.ORdonly;
            if (pathOnly)
                flags |= Native.OPath;
            return new DirFd(Native.open(path, flags));
        }

        public static DirFd OpenMatching(string path, UnixLstat expected, bool pathOnly = true)
        {
            var opened = Open(path, pathOnly);
            if (opened.Fd < 0)
                return opened;
            if (!Native.TryStatxFd(opened.Fd, out var got) || got.Device != expected.Device || got.Inode != expected.Inode)
            {
                opened.Dispose();
                Native.SetErrno(Native.ErrnoNoent);
                Marshal.SetLastPInvokeError(Native.ErrnoNoent);
                return new DirFd(-1);
            }

            return opened;
        }

        public void Dispose()
        {
            if (Fd >= 0)
                _ = Native.close(Fd);
        }
    }

    private static class Native
    {
        public const int AtFdcwd = -100;
        public const int AtSymlinkNofollow = 0x100;
        public const uint StatxBasicStats = 0x7ff;
        public const int ORdonly = 0;
        public const int ODirectory = 0x10000;   // 00200000
        public const int ONofollow = 0x20000;    // 00400000
        public const int OPath = 0x200000;       // 010000000
        public const uint RenameNoreplace = 1;
        public const int ErrnoNodata = 61;
        public const int ErrnoNotsup = 95;
        public const int ErrnoNosys = 38;
        public const int ErrnoNoent = 2;
        public const int ErrnoRange = 34;
        public const int AtEmptyPath = 0x1000;
        public const string PosixAclAccess = "system.posix_acl_access";

        private static readonly object PasswdLock = new();

        [DllImport("libc", SetLastError = true)]
        public static extern uint geteuid();

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern int statx(int dirfd, string pathname, int flags, uint mask, IntPtr statxbuf);

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern IntPtr realpath(string path, IntPtr resolved);

        [DllImport("libc")]
        public static extern void free(IntPtr ptr);

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern int open(string pathname, int flags);

        [DllImport("libc", SetLastError = true)]
        public static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        public static extern int fsync(int fd);

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern int renameat2(int olddirfd, string oldpath, int newdirfd, string newpath, uint flags);

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern int renameat(int olddirfd, string oldpath, int newdirfd, string newpath);

        [DllImport("libc", EntryPoint = "__errno_location")]
        private static extern IntPtr ErrnoLocation();

        public static void SetErrno(int value) => Marshal.WriteInt32(ErrnoLocation(), value);

        public static int GetErrno() => Marshal.ReadInt32(ErrnoLocation());

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern int linkat(int olddirfd, string oldpath, int newdirfd, string newpath, int flags);

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern int unlinkat(int dirfd, string pathname, int flags);

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern long lgetxattr(string path, string name, IntPtr value, nuint size);

        [DllImport("libc", SetLastError = true)]
        private static extern int getpwuid_r(uint uid, IntPtr pwd, IntPtr buf, nuint buflen, out IntPtr result);

        [DllImport("libc", SetLastError = true)]
        private static extern int getgrgid_r(uint gid, IntPtr grp, IntPtr buf, nuint buflen, out IntPtr result);

        [DllImport("libc")]
        private static extern void setpwent();

        [DllImport("libc")]
        private static extern void endpwent();

        [DllImport("libc")]
        private static extern IntPtr getpwent();

        public static bool TryStatx(string path, out UnixLstat info) =>
            TryStatxAt(AtFdcwd, path, out info);

        public static bool TryStatxFd(int fd, out UnixLstat info) =>
            TryStatxAt(fd, "", out info, AtEmptyPath);

        public static bool TryStatxAt(int dirfd, string path, out UnixLstat info, int extraFlags = 0)
        {
            info = default;
            var buf = Marshal.AllocHGlobal(512);
            try
            {
                NativeZero(buf, 512);
                if (statx(dirfd, path, AtSymlinkNofollow | extraFlags, StatxBasicStats, buf) != 0)
                    return false;
                // Layout of struct statx (linux/stat.h): uid at 20, gid at 24, mode at 28, ino at 32.
                var uid = (uint)Marshal.ReadInt32(buf, 20);
                var gid = (uint)Marshal.ReadInt32(buf, 24);
                var mode = (ushort)Marshal.ReadInt16(buf, 28);
                var ino = (ulong)Marshal.ReadInt64(buf, 32);
                // After four 16-byte timestamps (offset 64..127) come rdev major/minor (128/132)
                // then dev major/minor (136/140).
                var devMajor = (uint)Marshal.ReadInt32(buf, 136);
                var devMinor = (uint)Marshal.ReadInt32(buf, 140);
                info = new UnixLstat(MakeDev(devMajor, devMinor), ino, mode, uid, gid);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        public static bool TryGetPasswd(uint uid, out UnixPasswd passwd)
        {
            passwd = null!;
            var bufSize = 16 * 1024;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var pwd = Marshal.AllocHGlobal(64);
                var buf = Marshal.AllocHGlobal(bufSize);
                try
                {
                    NativeZero(pwd, 64);
                    var rc = getpwuid_r(uid, pwd, buf, (nuint)bufSize, out var result);
                    if (rc == ErrnoRange)
                    {
                        bufSize *= 2;
                        continue;
                    }

                    if (rc != 0 || result == IntPtr.Zero)
                        return false;
                    var parsed = ReadPasswd(result);
                    if (parsed is null)
                        return false;
                    passwd = parsed;
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                    Marshal.FreeHGlobal(pwd);
                }
            }

            return false;
        }

        public static bool TryGetGroup(uint gid, out UnixGroup group)
        {
            group = null!;
            var bufSize = 16 * 1024;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var grp = Marshal.AllocHGlobal(32);
                var buf = Marshal.AllocHGlobal(bufSize);
                try
                {
                    NativeZero(grp, 32);
                    var rc = getgrgid_r(gid, grp, buf, (nuint)bufSize, out var result);
                    if (rc == ErrnoRange)
                    {
                        bufSize *= 2;
                        continue;
                    }

                    if (rc != 0 || result == IntPtr.Zero)
                        return false;
                    var parsed = ReadGroup(result);
                    if (parsed is null)
                        return false;
                    group = parsed;
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                    Marshal.FreeHGlobal(grp);
                }
            }

            return false;
        }

        public static bool TryEnumeratePasswd(out IReadOnlyList<UnixPasswd> entries)
        {
            var list = new List<UnixPasswd>();
            lock (PasswdLock)
            {
                setpwent();
                try
                {
                    SetErrno(0);
                    while (true)
                    {
                        var ptr = getpwent();
                        if (ptr == IntPtr.Zero)
                        {
                            // errno != 0: enumeration failed (sssd/LDAP, etc.). Rules 1–3 suffice.
                            if (GetErrno() != 0)
                            {
                                entries = Array.Empty<UnixPasswd>();
                                return false;
                            }

                            break;
                        }

                        var parsed = ReadPasswd(ptr);
                        if (parsed is not null)
                            list.Add(parsed);
                    }
                }
                finally
                {
                    endpwent();
                }
            }

            entries = list;
            return true;
        }

        private static UnixPasswd? ReadPasswd(IntPtr ptr)
        {
            // struct passwd on Linux x86_64: name, passwd, uid, gid, gecos, dir, shell.
            var name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(ptr, 0));
            if (string.IsNullOrEmpty(name))
                return null;
            var uid = (uint)Marshal.ReadInt32(ptr, 16);
            var gid = (uint)Marshal.ReadInt32(ptr, 20);
            return new UnixPasswd(name, uid, gid);
        }

        private static UnixGroup? ReadGroup(IntPtr ptr)
        {
            // struct group on Linux x86_64: name, passwd, gid, gr_mem.
            var name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(ptr, 0));
            if (string.IsNullOrEmpty(name))
                return null;
            var gid = (uint)Marshal.ReadInt32(ptr, 16);
            var membersPtr = Marshal.ReadIntPtr(ptr, 24);
            var members = new List<string>();
            if (membersPtr != IntPtr.Zero)
            {
                for (var i = 0; ; i++)
                {
                    var item = Marshal.ReadIntPtr(membersPtr, i * IntPtr.Size);
                    if (item == IntPtr.Zero)
                        break;
                    var member = Marshal.PtrToStringUTF8(item);
                    if (!string.IsNullOrEmpty(member))
                        members.Add(member);
                }
            }

            return new UnixGroup(name, gid, members);
        }

        private static void NativeZero(IntPtr ptr, int size)
        {
            for (var i = 0; i < size; i++)
                Marshal.WriteByte(ptr, i, 0);
        }

        private static ulong MakeDev(uint major, uint minor) =>
            ((ulong)major << 8) | minor;
    }

    // Local aliases so AppImageTarget can keep calling Geteuid without extra usings.
    private static uint Geteuid() => Native.geteuid();
}
