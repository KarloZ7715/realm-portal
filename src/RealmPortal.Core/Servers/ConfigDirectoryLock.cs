namespace RealmPortal.Core.Servers;

/// <summary>
/// Exclusive cross-process lock for the Realm Portal config directory (<c>.realm-portal.lock</c>),
/// using the same open-with-<see cref="FileShare.None"/> pattern as client-file backups.
/// </summary>
public sealed class ConfigDirectoryLock(string configDirectory, TimeSpan? timeout = null)
{
    public const string LockFileName = ".realm-portal.lock";

    private readonly string _path = Path.Combine(configDirectory, LockFileName);
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(10);

    public IDisposable Acquire()
    {
        Directory.CreateDirectory(configDirectory);
        var deadline = DateTime.UtcNow + _timeout;
        while (true)
        {
            try
            {
                return new Handle(new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new StoreException(
                    $"Another Realm Portal process is using the config directory (or '{_path}' is not accessible): {ex.Message}",
                    ex);
            }
        }
    }

    private sealed class Handle(FileStream stream) : IDisposable
    {
        public void Dispose() => stream.Dispose();
    }
}
