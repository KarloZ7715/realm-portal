using System.Text;
using RealmPortal.Core.ClientFiles;

namespace RealmPortal.Core.IO;

/// <summary>
/// Bounded, non-following reads of files Realm Portal does not own (client and external launcher configuration).
/// A symbolic link, reparse point, FIFO or character device is refused, and nothing larger than the cap is read:
/// discovery must never hang on, or trust, a file it did not create.
/// </summary>
internal static class SafeFileRead
{
    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> from <paramref name="path"/> without following a final link.
    /// Returns false (and writes nothing) when the file is missing, not a regular seekable file, a link, or larger
    /// than <paramref name="maxBytes"/>; no exception escapes.
    /// </summary>
    internal static bool TryReadTextNonFollowing(string path, int maxBytes, out string text, out bool tooLarge)
    {
        text = string.Empty;
        tooLarge = false;
        if (string.IsNullOrWhiteSpace(path) || maxBytes <= 0)
            return false;

        try
        {
            using var stream = SymlinkSnapshot.OpenNoFollowForRead(path);
            if (!SymlinkSnapshot.IsSeekableRegularFile(stream, out var length))
                return false;
            if (length > maxBytes)
            {
                tooLarge = true;
                return false;
            }

            var buffer = new byte[maxBytes + 1];
            var read = 0;
            while (read < buffer.Length)
            {
                var count = stream.Read(buffer, read, buffer.Length - read);
                if (count == 0)
                    break;
                read += count;
            }

            if (read > maxBytes)
            {
                tooLarge = true;
                return false;
            }

            text = Encoding.UTF8.GetString(buffer, 0, read);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ClientFilesException
                                       or NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
