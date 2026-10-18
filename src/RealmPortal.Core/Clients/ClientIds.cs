using System.Security.Cryptography;
using System.Text;
using RealmPortal.Core.ClientFiles;

namespace RealmPortal.Core.Clients;

/// <summary>Stable client ids derived from folder paths (see docs/app-service.md).</summary>
public static class ClientIds
{
    /// <summary>
    /// <c>c</c> plus 16 lowercase hex digits of the SHA-256 of the canonical folder path
    /// (the same form as <see cref="ClientPaths.CanonicalRoot"/>, lowercased on Windows).
    /// </summary>
    public static string FromFolderPath(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var full = ClientPaths.TrimTrailingSeparators(Path.GetFullPath(folderPath));
        if (OperatingSystem.IsWindows())
            full = full.ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(full));
        return "c" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
