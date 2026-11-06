using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using RealmPortal.App;
using RealmPortal.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace RealmPortal.App.Tests;

/// <summary>Headless Avalonia host with real Skia rendering, so screenshots show what the app draws.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RealmPortal.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterPlatformServicesSetup(_ => EnsureRunLoopDispatcher());

    /// <summary>Times <see cref="EnsureRunLoopDispatcher"/> replaced a stray dispatcher in this test run.</summary>
    internal static int StrayDispatchersReplaced;

    /// <summary>
    /// Makes sure the headless test about to run gets the headless platform's UI dispatcher (#217).
    /// </summary>
    /// <remarks>
    /// Before every [AvaloniaFact] the headless session clears <see cref="Dispatcher.UIThread"/> (a process-wide static)
    /// and then registers the headless dispatcher. Background work left by an earlier test (a thread-pool continuation
    /// that posts to the UI thread) can read <see cref="Dispatcher.UIThread"/> in between; it then creates a dispatcher
    /// without the headless implementation, which cannot run nested loops, and the test fails with
    /// PlatformNotSupportedException from Dispatcher.PushFrame. Called right after the platform services are set up:
    /// if the dispatcher cannot run loops, clear it again so the next read creates the headless one.
    /// </remarks>
    internal static void EnsureRunLoopDispatcher()
    {
        if (Dispatcher.UIThread.SupportsRunLoops)
            return;

        Interlocked.Increment(ref StrayDispatchersReplaced);
        // Optional trace for measuring how often this happens: RP_TEST_DISPATCHER_LOG=<file>.
        if (Environment.GetEnvironmentVariable("RP_TEST_DISPATCHER_LOG") is { Length: > 0 } log)
            File.AppendAllText(log, $"{DateTime.UtcNow:O} replaced a stray UI dispatcher{Environment.NewLine}");
        ResetUIThreadDispatcher();
        if (!Dispatcher.UIThread.SupportsRunLoops)
            throw new InvalidOperationException("The UI dispatcher cannot run loops even after a reset; is the headless platform registered?");
    }

    /// <summary>Clears <see cref="Dispatcher.UIThread"/> through Avalonia's internal unit-test reset.</summary>
    internal static void ResetUIThreadDispatcher()
    {
        var reset = typeof(Dispatcher).GetMethod("ResetForUnitTests", BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(typeof(Dispatcher).FullName, "ResetForUnitTests");
        reset.Invoke(null, null);
    }
}
