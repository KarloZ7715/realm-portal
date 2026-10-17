using System.Security.Cryptography;
using System.Text;

namespace RealmPortal.Core.Clients;

/// <summary>Stable client ids derived from folder paths (see docs/app-service.md).</summary>
public static class ClientIds
{
    /// <summary><c>c</c> plus 16 lowercase hex digits of the SHA-256 of the full path (lowercased on Windows).</summary>
    public static string FromFolderPath(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var full = Path.GetFullPath(folderPath);
        if (OperatingSystem.IsWindows())
            full = full.ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(full));
        return "c" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
