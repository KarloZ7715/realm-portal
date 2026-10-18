using RealmPortal.Core.Servers;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.Service;

/// <summary>Size limits and permissions for small JSON config files written by the service.</summary>
internal static class PortalConfigFile
{
    public const int MaxBytes = 16 * 1024 * 1024;

    private static readonly object ConfigLock = new();

    public static void WithConfigLock(Action work)
    {
        lock (ConfigLock)
        {
            work();
        }
    }

    public static T WithConfigLock<T>(Func<T> work)
    {
        lock (ConfigLock)
        {
            return work();
        }
    }

    public static byte[] ReadBounded(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(path);

        return PrivateStorage.ReadBounded(path, MaxBytes);
    }

    public static void AfterWrite(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (IOException ex)
            {
                throw new StoreException($"Cannot set permissions on '{path}': {ex.Message}", ex);
            }
        }
    }
}
