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
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var target = _decorateStream?.Invoke(tempPath, stream) ?? stream;
                target.Write(content, 0, content.Length);
                target.Flush();
                stream.Flush(flushToDisk: true);
            }

            ApplyPermissionsBeforeReplace(tempPath, fullPath, permissions);
            File.Move(tempPath, fullPath, overwrite: true);
            ApplyPermissionsAfterReplace(fullPath, permissions);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    private static void ApplyPermissionsBeforeReplace(string tempPath, string destinationPath, FileReplacementPermissions? permissions)
    {
        if (permissions is null)
            return;

        if (permissions.UnixMode is int unix && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            && FileReplacementPermissions.TryNormalizeUnixMode(unix, out var normalized))
        {
            File.SetUnixFileMode(tempPath, (UnixFileMode)(normalized & FileReplacementPermissions.UnixPermissionMask));
        }

        if (OperatingSystem.IsWindows() && permissions.WindowsReadOnly is true)
        {
            if (File.Exists(destinationPath))
            {
                var destAttrs = File.GetAttributes(destinationPath);
                if (destAttrs.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(destinationPath, destAttrs & ~FileAttributes.ReadOnly);
            }

            var tempAttrs = File.GetAttributes(tempPath);
            File.SetAttributes(tempPath, tempAttrs | FileAttributes.ReadOnly);
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
