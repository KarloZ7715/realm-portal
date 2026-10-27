using System.Runtime.CompilerServices;
using RealmPortal.Core;

namespace RealmPortal.App.Tests.Support;

/// <summary>
/// Points <see cref="ExceptionLog.Default"/> at a crash log under the temp path before any test runs.
/// ViewModel fire-and-forget work that calls <see cref="ExceptionLog.Observe"/> therefore never
/// writes into the user's real state directory.
/// </summary>
internal static class TestExceptionLogGuard
{
    internal static readonly string Sentinel = Path.Combine(
        Path.GetTempPath(),
        "realm-portal-test-exception-log-" + Environment.ProcessId);

    internal static readonly string LogPath = Path.Combine(Sentinel, ExceptionLog.FileName);

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Sentinel);
        ExceptionLog.SetDefaultForTests(new ExceptionLog(
            logPath: () => LogPath,
            version: () => "0.0.0-test"));
    }
}

/// <summary>Confirms the process default logger writes under the temp sentinel, not the user state path.</summary>
public sealed class ExceptionLogGuardTests
{
    [Fact]
    public void Guard_DefaultLoggerWritesUnderTempSentinel()
    {
        var marker = "exception-log-guard-" + Guid.NewGuid().ToString("N");
        ExceptionLog.Default.WriteMessage(marker, "GuardTest");

        Assert.True(File.Exists(TestExceptionLogGuard.LogPath));
        var text = File.ReadAllText(TestExceptionLogGuard.LogPath);
        Assert.Contains(marker, text, StringComparison.Ordinal);

        var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var logFull = Path.GetFullPath(TestExceptionLogGuard.LogPath);
        Assert.StartsWith(tempRoot + Path.DirectorySeparatorChar, logFull, StringComparison.Ordinal);

        // PortalPaths still reports the real user path when REALM_PORTAL_STATE_DIR is unset;
        // Default must not point there. Do not read that file.
        var userCrash = Path.GetFullPath(PortalPaths.CrashLogFile());
        Assert.NotEqual(userCrash, logFull);
    }
}
