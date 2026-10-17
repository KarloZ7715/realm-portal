using System.Security.Cryptography;

namespace RealmPortal.Core.ClientFiles;

/// <summary>Small helpers shared by the planner and the applier.</summary>
internal static class ClientFilesIo
{
    public static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Maps an I/O exception to a typed error (no retries are ever attempted on permission errors).</summary>
    public static ClientFilesException Map(Exception ex, string path, string action)
    {
        var code = ex switch
        {
            UnauthorizedAccessException => ClientFilesErrorCode.PermissionDenied,
            DirectoryNotFoundException or FileNotFoundException or PathTooLongException
                or ArgumentException or NotSupportedException => ClientFilesErrorCode.PathNotFound,
            IOException io when IsReadOnlyFileSystem(io) => ClientFilesErrorCode.ReadOnlyFs,
            // Other I/O failures on a file we could see a moment ago are almost always access problems (locks, ACLs).
            _ => ClientFilesErrorCode.PermissionDenied,
        };
        return new ClientFilesException(code, path, $"Cannot {action} '{path}': {ex.Message}", ex);
    }

    private static bool IsReadOnlyFileSystem(IOException ex) =>
        // Linux EROFS (30) and Windows ERROR_WRITE_PROTECT (19), as reported through HResult.
        ex.HResult == 30 || (ex.HResult & 0xFFFF) == 19 && (ex.HResult & 0x7FFF0000) == 0x70000;
}
