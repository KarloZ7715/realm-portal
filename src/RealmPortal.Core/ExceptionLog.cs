using System.Globalization;
using System.Text;
using RealmPortal.Core.ClientFiles;
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
    private const string ClientPathToken = "[client-path]";

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
            var target = log ?? Default;
            var exception = task.Exception!;
            _ = Task.Run(() => target.Write(exception, "BackgroundTask"));
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
        WriteMessage(FormatException(exception), source, CollectClientPaths(exception));
    }

    public void WriteMessage(string message, string? source = null)
        => WriteMessage(message, source, extraPaths: null);

    private void WriteMessage(string message, string? source, IReadOnlyList<string>? extraPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        try
        {
            var entry = FormatEntry(Redact(message, extraPaths), source);
            lock (_gate)
                AppendEntry(entry);
        }
        catch
        {
            // Logging must never throw into the caller or take down the process.
        }
    }

    internal string Redact(string text, IReadOnlyList<string>? extraPaths = null)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = text;

        // Client paths first so a path under home is fully replaced before "~" shortens
        // the prefix and leaves a personal suffix (e.g. an email segment) in the log.
        if (extraPaths is not null)
        {
            foreach (var path in extraPaths)
                result = RedactClientPath(result, path);
        }

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

    private static string RedactClientPath(string text, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 2)
            return text;

        var result = text;
        if (Path.IsPathRooted(path))
        {
            try
            {
                var full = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                result = ReplacePath(result, full, ClientPathToken);
                result = ReplacePath(result, full + Path.DirectorySeparatorChar, ClientPathToken + Path.DirectorySeparatorChar);
                if (Path.DirectorySeparatorChar != '/')
                {
                    var slash = full.Replace(Path.DirectorySeparatorChar, '/');
                    result = ReplacePath(result, slash, ClientPathToken);
                    result = ReplacePath(result, slash + "/", ClientPathToken + "/");
                }
            }
            catch
            {
                // Fall through to the literal path form below.
            }
        }

        return ReplacePath(result, path, ClientPathToken);
    }

    private static IReadOnlyList<string> CollectClientPaths(Exception exception)
    {
        var paths = new List<string>();
        var seen = new HashSet<Exception>();
        var stack = new Stack<Exception>();
        stack.Push(exception);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
                continue;

            if (current is ClientFilesException clientFiles && !string.IsNullOrWhiteSpace(clientFiles.Path))
                paths.Add(clientFiles.Path);

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    stack.Push(inner);
            }
            else if (current.InnerException is not null)
            {
                stack.Push(current.InnerException);
            }
        }

        return paths;
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
        return header + Environment.NewLine + EscapeControls(body.TrimEnd()) + Environment.NewLine + Environment.NewLine;
    }

    private static string EscapeControls(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            switch (character)
            {
                case '\r':
                    result.Append("\\r");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                case (char)0x1B:
                    result.Append("\\x1B");
                    break;
                case '\u2028':
                    result.Append("\\u2028");
                    break;
                case '\u2029':
                    result.Append("\\u2029");
                    break;
                default:
                    if (char.IsControl(character))
                        result.Append($"\\u{(int)character:X4}");
                    else
                        result.Append(character);
                    break;
            }
        }

        return result.ToString();
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
        if (bytes.LongLength > _maxBytes)
            bytes = TruncateUtf8(bytes, _maxBytes);

        if (File.Exists(path))
        {
            var length = new FileInfo(path).Length;
            if (length > 0 && length + bytes.LongLength > _maxBytes)
                Rotate(path);
        }

        WriteAppend(path, bytes);
        EnsurePrivateFileMode(path);
    }

    /// <summary>Shortens <paramref name="bytes"/> to at most <paramref name="maxBytes"/> without splitting a UTF-8 code unit.</summary>
    private static byte[] TruncateUtf8(byte[] bytes, long maxBytes)
    {
        if (bytes.LongLength <= maxBytes)
            return bytes;

        var limit = (int)maxBytes;
        while (limit > 0 && (bytes[limit] & 0xC0) == 0x80)
            limit--;

        if (limit == bytes.Length)
            return bytes;

        var truncated = new byte[limit];
        Buffer.BlockCopy(bytes, 0, truncated, 0, limit);
        return truncated;
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
