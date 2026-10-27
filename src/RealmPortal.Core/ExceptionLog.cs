using System.Globalization;
using System.Text;
using RealmPortal.Core.IO;

namespace RealmPortal.Core;

/// <summary>
/// Appends exception reports to the local crash log defined in <c>docs/updates-and-status.md</c>
/// (no network). Thread-safe; failures while writing are swallowed so logging never takes down the process.
/// </summary>
public sealed class ExceptionLog
{
    public const long MaxBytes = 1L << 20; // 1 MiB
    public const string FileName = "realm-portal.log";
    public const string RotatedSuffix = ".1";

    private static readonly object DefaultGate = new();
    private static ExceptionLog? _default;

    private readonly object _gate = new();
    private readonly Func<string> _logPath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string> _version;
    private readonly Func<string?> _homeDirectory;
    private readonly long _maxBytes;

    /// <summary>Process-wide logger writing to <see cref="PortalPaths.CrashLogFile"/>.</summary>
    public static ExceptionLog Default
    {
        get
        {
            lock (DefaultGate)
                return _default ??= new ExceptionLog();
        }
    }

    /// <summary>Replaces <see cref="Default"/> (tests). Pass <c>null</c> to restore the path-based default.</summary>
    public static void SetDefaultForTests(ExceptionLog? log)
    {
        lock (DefaultGate)
            _default = log;
    }

    public ExceptionLog(
        Func<string>? logPath = null,
        Func<DateTimeOffset>? clock = null,
        Func<string>? version = null,
        Func<string?>? homeDirectory = null,
        long maxBytes = MaxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        _logPath = logPath ?? (() => PortalPaths.CrashLogFile());
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _version = version ?? (() => AppInfo.Version);
        _homeDirectory = homeDirectory ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _maxBytes = maxBytes;
    }

    /// <summary>
    /// Registers <see cref="AppDomain.UnhandledException"/> and <see cref="TaskScheduler.UnobservedTaskException"/>
    /// so crashes and abandoned task faults land in the crash log. Safe to call once at process start.
    /// </summary>
    public static void InstallProcessHandlers(ExceptionLog? log = null)
    {
        var target = log ?? Default;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                target.Write(ex, "UnhandledException");
            else if (e.ExceptionObject is not null)
                target.WriteMessage(e.ExceptionObject.ToString() ?? "(unknown)", "UnhandledException");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            target.Write(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        };
    }

    /// <summary>Observes a fire-and-forget task and writes any fault to the crash log.</summary>
    public static void Observe(Task? task, ExceptionLog? log = null)
    {
        if (task is null)
            return;
        if (task.IsCompletedSuccessfully)
            return;
        if (task.IsFaulted)
        {
            (log ?? Default).Write(task.Exception!, "BackgroundTask");
            return;
        }

        _ = ObserveAsync(task, log ?? Default);
    }

    private static async Task ObserveAsync(Task task, ExceptionLog log)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Write(ex, "BackgroundTask");
        }
    }

    public void Write(Exception exception, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        WriteMessage(FormatException(exception), source);
    }

    public void WriteMessage(string message, string? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        try
        {
            var entry = FormatEntry(Redact(message), source);
            lock (_gate)
                AppendEntry(entry);
        }
        catch
        {
            // Logging must never throw into the caller or take down the process.
        }
    }

    internal string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = text;
        var home = _homeDirectory();
        if (!string.IsNullOrWhiteSpace(home))
        {
            var fullHome = Path.GetFullPath(home.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            result = ReplacePath(result, fullHome, "~");
            // Exception messages sometimes keep the trailing separator form.
            result = ReplacePath(result, fullHome + Path.DirectorySeparatorChar, "~" + Path.DirectorySeparatorChar);
            if (Path.DirectorySeparatorChar != '/')
            {
                result = ReplacePath(result, fullHome.Replace(Path.DirectorySeparatorChar, '/'), "~");
                result = ReplacePath(result, fullHome.Replace(Path.DirectorySeparatorChar, '/') + "/", "~/");
            }
        }

        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImage))
            result = ReplacePath(result, appImage, Path.GetFileName(appImage));

        return result;
    }

    private static string ReplacePath(string text, string path, string replacement)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 2)
            return text;
        return text.Replace(path, replacement, StringComparison.OrdinalIgnoreCase);
    }

    private string FormatEntry(string body, string? source)
    {
        var stamp = _clock().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var header = source is null
            ? $"[{stamp}] {AppInfo.Name} {_version()}"
            : $"[{stamp}] {AppInfo.Name} {_version()} ({source})";
        return header + Environment.NewLine + body.TrimEnd() + Environment.NewLine + Environment.NewLine;
    }

    private static string FormatException(Exception exception)
    {
        // Exception.ToString() already includes type, message, stack and inner exceptions.
        return exception.ToString();
    }

    private void AppendEntry(string entry)
    {
        var path = _logPath();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            PrivateStorage.CreatePrivateDirectory(directory);

        var bytes = Encoding.UTF8.GetBytes(entry);
        if (File.Exists(path))
        {
            var length = new FileInfo(path).Length;
            if (length > 0 && length + bytes.LongLength > _maxBytes)
                Rotate(path);
        }

        WriteAppend(path, bytes);
        EnsurePrivateFileMode(path);
    }

    private static void Rotate(string path)
    {
        var rotated = path + RotatedSuffix;
        try
        {
            if (File.Exists(rotated))
                File.Delete(rotated);
            File.Move(path, rotated);
            EnsurePrivateFileMode(rotated);
        }
        catch
        {
            // If rotation fails, still try to append (may exceed the cap once).
        }
    }

    private static void WriteAppend(string path, byte[] bytes)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.None,
        };
        if (!OperatingSystem.IsWindows() && !File.Exists(path))
            options.UnixCreateMode = PrivateStorage.PrivateFileMode;

        using var stream = new FileStream(path, options);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void EnsurePrivateFileMode(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
            return;
        try
        {
            File.SetUnixFileMode(path, PrivateStorage.PrivateFileMode);
        }
        catch
        {
            // Best-effort; the create mode already requested 0600 when the file was new.
        }
    }
}
