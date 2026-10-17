namespace RealmPortal.Core.IO;

/// <summary>
/// Writes to a temporary file in the destination directory, flushes it to disk and renames it over the target
/// (<see cref="File.Move(string, string, bool)"/> with overwrite, which is an atomic replace on Linux and Windows
/// when both paths are on the same volume — guaranteed here because the temp file lives next to the target).
/// </summary>
public sealed class AtomicFileWriter : IAtomicFileWriter
{
    private readonly Func<string, Stream, Stream>? _decorateStream;

    public AtomicFileWriter()
    {
    }

    /// <summary>
    /// Test seam: <paramref name="decorateStream"/> receives the path of the temporary file and its stream and returns the stream the
    /// content is written to, so tests can inject faults in the middle of a write (for example a stream that throws after N bytes).
    /// The original stream is still the one that is flushed to disk and disposed.
    /// </summary>
    public AtomicFileWriter(Func<string, Stream, Stream> decorateStream)
    {
        ArgumentNullException.ThrowIfNull(decorateStream);
        _decorateStream = decorateStream;
    }

    public void WriteAllBytes(string path, byte[] content) => WriteAllBytes(path, content, null);

    public void WriteAllBytes(string path, byte[] content, FileReplacementPermissions? permissions)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var clearedDestinationReadOnly = false;
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var target = _decorateStream?.Invoke(tempPath, stream) ?? stream;
                target.Write(content, 0, content.Length);
                target.Flush();
                stream.Flush(flushToDisk: true);
            }

            ApplyPermissionsBeforeReplace(tempPath, fullPath, permissions, ref clearedDestinationReadOnly);
            File.Move(tempPath, fullPath, overwrite: true);
            ApplyPermissionsAfterReplace(fullPath, permissions);
        }
        catch
        {
            // The replace did not happen: put the destination's read-only attribute back and remove the temp file
            // (on Windows a read-only temp file cannot be deleted until the attribute is cleared).
            TryRestoreReadOnly(fullPath, clearedDestinationReadOnly);
            TryDeleteTemp(tempPath);
            throw;
        }
    }

    private static void TryRestoreReadOnly(string destinationPath, bool cleared)
    {
        if (!cleared)
            return;
        try
        {
            if (File.Exists(destinationPath))
                File.SetAttributes(destinationPath, File.GetAttributes(destinationPath) | FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (!File.Exists(tempPath))
                return;
            if (OperatingSystem.IsWindows())
            {
                var attrs = File.GetAttributes(tempPath);
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(tempPath, attrs & ~FileAttributes.ReadOnly);
            }

            File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void ApplyPermissionsBeforeReplace(
        string tempPath, string destinationPath, FileReplacementPermissions? permissions, ref bool clearedDestinationReadOnly)
    {
        if (permissions is null)
            return;

        if (permissions.UnixMode is int unix && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
        {
            // Re-check here too: a caller-built FileReplacementPermissions must never set setuid/setgid/sticky bits.
            if (!FileReplacementPermissions.IsValidUnixMode(unix))
                throw new ArgumentOutOfRangeException(nameof(permissions), unix, "A Unix mode must be within 0..0777.");
            File.SetUnixFileMode(tempPath, (UnixFileMode)(unix & FileReplacementPermissions.UnixPermissionMask));
        }

        if (OperatingSystem.IsWindows() && permissions.WindowsReadOnly is true)
        {
            // Make the temp file read-only first, so the file that lands at the destination already has the attribute:
            // there is no moment (and no failure point after the rename) where the replaced file is writable.
            File.SetAttributes(tempPath, File.GetAttributes(tempPath) | FileAttributes.ReadOnly);

            if (File.Exists(destinationPath))
            {
                var destAttrs = File.GetAttributes(destinationPath);
                if (destAttrs.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(destinationPath, destAttrs & ~FileAttributes.ReadOnly);
                    clearedDestinationReadOnly = true;
                }
            }
        }
    }

    private static void ApplyPermissionsAfterReplace(string destinationPath, FileReplacementPermissions? permissions)
    {
        if (permissions?.WindowsReadOnly is true && OperatingSystem.IsWindows())
        {
            var attrs = File.GetAttributes(destinationPath);
            File.SetAttributes(destinationPath, attrs | FileAttributes.ReadOnly);
        }
    }
}
