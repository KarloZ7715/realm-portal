using RealmPortal.Core;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ExceptionLogTests
{
    [Fact]
    public async Task Observe_LogsFaultFromBackgroundTask()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine(ExceptionLog.FileName);
        var log = new ExceptionLog(logPath: () => path, version: () => "9.9.9-test");

        ExceptionLog.Observe(FailAsync(), log);
        await WaitForAsync(() => File.Exists(path) && File.ReadAllText(path).Length > 0);

        var text = File.ReadAllText(path);
        Assert.Contains("BackgroundTask", text, StringComparison.Ordinal);
        Assert.Contains("9.9.9-test", text, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), text, StringComparison.Ordinal);
        Assert.Contains("boom-from-background", text, StringComparison.Ordinal);
        Assert.Contains("at RealmPortal.Tests.ExceptionLogTests", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Observe_AlreadyFaulted_WritesOffCallerThread()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine(ExceptionLog.FileName);
        var log = new ExceptionLog(logPath: () => path, version: () => "9.9.9-test");

        var faulted = Task.FromException(new InvalidOperationException("already-faulted"));
        ExceptionLog.Observe(faulted, log);
        await WaitForAsync(() => File.Exists(path) && File.ReadAllText(path).Length > 0);

        var text = File.ReadAllText(path);
        Assert.Contains("BackgroundTask", text, StringComparison.Ordinal);
        Assert.Contains("already-faulted", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RotatesWhenCapExceeded()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine(ExceptionLog.FileName);
        var log = new ExceptionLog(logPath: () => path, version: () => "1.0.0", maxBytes: 256);

        log.WriteMessage(new string('A', 200), "first");
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ExceptionLog.RotatedSuffix));

        log.WriteMessage(new string('B', 200), "second");
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ExceptionLog.RotatedSuffix));

        var current = File.ReadAllText(path);
        var rotated = File.ReadAllText(path + ExceptionLog.RotatedSuffix);
        Assert.Contains("second", current, StringComparison.Ordinal);
        Assert.DoesNotContain("first", current, StringComparison.Ordinal);
        Assert.Contains("first", rotated, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_TruncatesSingleEntryLargerThanCap()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine(ExceptionLog.FileName);
        const int maxBytes = 128;
        var log = new ExceptionLog(logPath: () => path, version: () => "1.0.0", maxBytes: maxBytes);

        log.WriteMessage(new string('Z', 400), "huge");

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ExceptionLog.RotatedSuffix));
        var length = new FileInfo(path).Length;
        Assert.True(length <= maxBytes, $"log length {length} exceeded cap {maxBytes}");
        var text = File.ReadAllText(path);
        Assert.Contains("huge", text, StringComparison.Ordinal);
        Assert.Contains("Z", text, StringComparison.Ordinal);
    }

    [NonPrivilegedUnixFact]
    public void Write_CreatesPrivateDirectoryAndFileModes()
    {
        using var dir = new TempDirectory();
        var logs = dir.Combine("nested", "logs");
        var path = Path.Combine(logs, ExceptionLog.FileName);
        var log = new ExceptionLog(logPath: () => path, version: () => "1.0.0");

        log.Write(new InvalidOperationException("mode-check"));

        Assert.Equal(PrivateStorage.PrivateDirectoryMode, File.GetUnixFileMode(logs));
        Assert.Equal(PrivateStorage.PrivateFileMode, File.GetUnixFileMode(path));
    }

    [Fact]
    public void Write_RedactsHomeDirectoryPaths()
    {
        using var dir = new TempDirectory();
        var home = dir.Combine("home", "player");
        Directory.CreateDirectory(home);
        var path = dir.Combine(ExceptionLog.FileName);
        var log = new ExceptionLog(
            logPath: () => path,
            version: () => "1.0.0",
            homeDirectory: () => home);

        var secret = Path.Combine(home, ".config", "wow-servidores", "activo.json");
        log.Write(new IOException($"Failed to read '{secret}'"));

        var text = File.ReadAllText(path);
        Assert.DoesNotContain(home, text, StringComparison.Ordinal);
        Assert.Contains("~" + Path.DirectorySeparatorChar + ".config", text, StringComparison.Ordinal);
        Assert.Contains("activo.json", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RedactsClientFilesExceptionPathOutsideHome()
    {
        using var dir = new TempDirectory();
        var home = dir.Combine("home", "player");
        Directory.CreateDirectory(home);
        var path = dir.Combine(ExceptionLog.FileName);
        var log = new ExceptionLog(
            logPath: () => path,
            version: () => "1.0.0",
            homeDirectory: () => home);

        var clientPath = Path.Combine(dir.Path, "media", "Alice", "WoW");
        Directory.CreateDirectory(clientPath);
        var ex = new ClientFilesException(
            ClientFilesErrorCode.PathNotFound,
            clientPath,
            $"The client directory '{clientPath}' does not exist.");
        log.Write(ex);

        var text = File.ReadAllText(path);
        Assert.DoesNotContain(clientPath, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(dir.Path, "media", "Alice"), text, StringComparison.Ordinal);
        Assert.Contains("[client-path]", text, StringComparison.Ordinal);
        Assert.Contains(nameof(ClientFilesException), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RedactsClientPathUnderHomeBeforeTildeShortening()
    {
        using var dir = new TempDirectory();
        var home = dir.Combine("home", "player");
        Directory.CreateDirectory(home);
        var path = dir.Combine(ExceptionLog.FileName);
        var log = new ExceptionLog(
            logPath: () => path,
            version: () => "1.0.0",
            homeDirectory: () => home);

        var clientPath = Path.Combine(home, "Games", "alice@example.com", "WoW");
        Directory.CreateDirectory(clientPath);
        log.Write(new ClientFilesException(
            ClientFilesErrorCode.PathNotFound,
            clientPath,
            $"The client directory '{clientPath}' does not exist."));

        var text = File.ReadAllText(path);
        Assert.DoesNotContain(clientPath, text, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@example.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine("Games", "alice@example.com"), text, StringComparison.Ordinal);
        Assert.Contains("[client-path]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RedactsClientPathInsideAggregateException()
    {
        using var dir = new TempDirectory();
        var home = dir.Combine("home", "u");
        Directory.CreateDirectory(home);
        var path = dir.Combine(ExceptionLog.FileName);
        var log = new ExceptionLog(
            logPath: () => path,
            version: () => "1.0.0",
            homeDirectory: () => home);

        var clientPath = "/media/Alice/WoW";
        var inner = new ClientFilesException(
            ClientFilesErrorCode.PermissionDenied,
            clientPath,
            $"Access denied for '{clientPath}'");
        log.Write(new AggregateException("batch", inner));

        var text = File.ReadAllText(path);
        Assert.DoesNotContain(clientPath, text, StringComparison.Ordinal);
        Assert.DoesNotContain("/media/Alice", text, StringComparison.Ordinal);
        Assert.Contains("[client-path]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_EscapesControlCharactersInBody()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine(ExceptionLog.FileName);
        var log = new ExceptionLog(logPath: () => path, version: () => "1.0.0");

        log.WriteMessage("line1\r\nline2\t" + (char)0x1B + "[31mred\u2028sep", "ctrl");

        var text = File.ReadAllText(path);
        Assert.Contains("\\r", text, StringComparison.Ordinal);
        Assert.Contains("\\n", text, StringComparison.Ordinal);
        Assert.Contains("\\t", text, StringComparison.Ordinal);
        Assert.Contains("\\x1B", text, StringComparison.Ordinal);
        Assert.Contains("\\u2028", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nline2", text, StringComparison.Ordinal);
        // Header/body separators remain real newlines; injected body newlines do not.
        Assert.Contains("line1\\r\\nline2\\t\\x1B", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_ReplacesAppImagePathWithFileName()
    {
        using var dir = new TempDirectory();
        var appImage = dir.Combine("opt", "RealmPortal.AppImage");
        Directory.CreateDirectory(Path.GetDirectoryName(appImage)!);
        File.WriteAllText(appImage, "x");
        var previous = Environment.GetEnvironmentVariable("APPIMAGE");
        Environment.SetEnvironmentVariable("APPIMAGE", appImage);
        try
        {
            var log = new ExceptionLog(
                logPath: () => dir.Combine(ExceptionLog.FileName),
                version: () => "1.0.0",
                homeDirectory: () => dir.Combine("home", "u"));
            var redacted = log.Redact($"updating {appImage}");
            Assert.Equal("updating RealmPortal.AppImage", redacted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPIMAGE", previous);
        }
    }

    [Fact]
    public void PortalPaths_CrashLogFile_HonoursStateDirOverride()
    {
        string? Env(string name) => name switch
        {
            PortalPaths.StateDirVariable => "/tmp/rp-state",
            _ => null,
        };

        Assert.Equal(
            Path.Combine("/tmp/rp-state", "logs", ExceptionLog.FileName),
            PortalPaths.CrashLogFile(Env));
    }

    [UnixFact]
    public void PortalPaths_LogDirectory_UsesXdgStateHomeOnUnix()
    {
        string? Env(string name) => name switch
        {
            "XDG_STATE_HOME" => "/xdg/state",
            _ => null,
        };

        Assert.Equal(Path.Combine("/xdg/state", "realm-portal", "logs"), PortalPaths.LogDirectory(Env));
    }

    private static async Task FailAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("boom-from-background");
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }

        Assert.Fail("condition was not met in time");
    }
}
