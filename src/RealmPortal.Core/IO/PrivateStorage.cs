using RealmPortal.Core.Servers;

namespace RealmPortal.Core.IO;

/// <summary>Permission and size helpers for files owned by Realm Portal.</summary>
public static class PrivateStorage
{
    public const int MaxJsonBytes = 16 * 1024 * 1024;
    internal const int MaxExecutableBytes = 64 * 1024 * 1024;
    internal const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    internal const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static FileReplacementPermissions PrivateFilePermissions { get; } = new((int)PrivateFileMode, null);

    internal static DirectoryInfo CreatePrivateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var existed = Directory.Exists(path);
        var directory = OperatingSystem.IsWindows()
            ? Directory.CreateDirectory(path)
            : Directory.CreateDirectory(path, PrivateDirectoryMode);
        if (!existed && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory.FullName, PrivateDirectoryMode);
        return directory;
    }

    public static byte[] ReadBounded(string path, int maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maxBytes)
            throw new StoreException($"'{path}' exceeds the maximum size of {maxBytes} bytes.");

        using var output = new MemoryStream((int)stream.Length);
        var buffer = new byte[81920];
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            if (output.Length + read > maxBytes)
                throw new StoreException($"'{path}' exceeds the maximum size of {maxBytes} bytes.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
