namespace RealmPortal.Core.IO;

/// <summary>
/// Writes to a temporary file in the destination directory, flushes it to disk and renames it over the target
/// (<see cref="File.Move(string, string, bool)"/> with overwrite, which is an atomic replace on Linux and Windows
/// when both paths are on the same volume — guaranteed here because the temp file lives next to the target).
/// </summary>
public sealed class AtomicFileWriter : IAtomicFileWriter
{
    public void WriteAllBytes(string path, byte[] content)
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
                stream.Write(content, 0, content.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite: true);
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
}
