namespace RealmPortal.Core.Servers;

/// <summary>
/// Exclusive lock for the Realm Portal config directory (<c>.realm-portal.lock</c>). Every read-modify-write
/// sequence on config files takes it, so parallel CLI/App processes cannot lose servers or leave
/// <c>activo.json</c> out of sync with <c>state.json</c>.
/// </summary>
/// <remarks>
/// Acquisition order is always the same: first a process-wide gate (a monitor keyed by the lock path), then the
/// lock file opened with <see cref="FileShare.None"/> (<c>flock</c> on Unix), matching client-file backups.
/// The gate makes contention inside one process wait instead of failing, and the file makes contention across
/// processes wait too. Both honour <paramref name="timeout"/> (10 s by default); expiring raises
/// <see cref="StoreException"/> with <see cref="StoreException.Locked"/> set. The gate is reentrant, so a
/// method may nest helpers that take the same lock again on the same thread.
/// </remarks>
public sealed class ConfigDirectoryLock
{
    public const string LockFileName = ".realm-portal.lock";

    private static readonly object GatesLock = new();
    private static readonly Dictionary<string, object> Gates = new(StringComparer.Ordinal);
    [ThreadStatic] private static HashSet<string>? _held;

    private readonly string _path;
    private readonly string _directory;
    private readonly object _gate;
    private readonly TimeSpan _timeout;

    public ConfigDirectoryLock(string configDirectory, TimeSpan? timeout = null)
    {
        _directory = Path.GetFullPath(configDirectory);
        _path = Path.Combine(_directory, LockFileName);
        _gate = GateFor(_path);
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public IDisposable Acquire()
    {
        if (_held?.Contains(_path) == true)
            return new Handle(null);

        var deadline = DateTime.UtcNow + _timeout;
        var remaining = deadline - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        if (!Monitor.TryEnter(_gate, remaining))
            throw Locked(null);

        try
        {
            CreateDirectory();
            var stream = Open(deadline);
            (_held ??= []).Add(_path);
            return new Handle(this, stream);
        }
        catch
        {
            Monitor.Exit(_gate);
            throw;
        }
    }

    private void CreateDirectory()
    {
        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Runs inside the handled acquisition path: a config path that is a file, or a parent that denies
            // access, surfaces as StoreException like every other acquisition failure.
            throw new StoreException($"The config directory '{_directory}' cannot be created: {ex.Message}", ex);
        }
    }

    private FileStream Open(DateTime deadline)
    {
        while (true)
        {
            try
            {
                return new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw Locked(ex);
            }
        }
    }

    private StoreException Locked(Exception? inner) =>
        new(
            $"Another Realm Portal process is using the config directory (or '{_path}' is not accessible"
            + $"{(inner is null ? string.Empty : ": " + inner.Message)}).",
            inner,
            locked: true);

    private static object GateFor(string path)
    {
        lock (GatesLock)
        {
            if (!Gates.TryGetValue(path, out var gate))
                Gates[path] = gate = new object();
            return gate;
        }
    }

    private sealed class Handle : IDisposable
    {
        private readonly ConfigDirectoryLock? _owner;
        private readonly FileStream? _stream;

        public Handle(ConfigDirectoryLock? owner) => _owner = owner;

        public Handle(ConfigDirectoryLock owner, FileStream stream)
        {
            _owner = owner;
            _stream = stream;
        }

        public void Dispose()
        {
            if (_owner is null)
                return;

            _stream?.Dispose();
            _held?.Remove(_owner._path);
            Monitor.Exit(_owner._gate);
        }
    }
}
