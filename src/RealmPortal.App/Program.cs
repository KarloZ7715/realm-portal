using Avalonia;
using RealmPortal.App.Platform;
using RealmPortal.Core;
using RealmPortal.Core.Instances;

namespace RealmPortal.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => Run(args, StartupSteps.Default);

    /// <summary>
    /// The startup sequence, in this order: crash log handlers (#178) first, so a failure anywhere after them is
    /// logged; then the single-instance claim (#197), which may end the process because another window took over;
    /// then the UI. Kept separate from <see cref="Main"/> so tests can check the order with fake steps.
    /// </summary>
    internal static int Run(string[] args, StartupSteps steps)
    {
        steps.InstallCrashLog(null);
        using var instance = steps.ClaimInstance(args);
        if (instance.ShouldExit)
            return 0;
        // SIGTERM (logout, `kill`) does not unwind Main: remove the socket there too. A leftover is harmless anyway.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => instance.Dispose();
        return steps.StartUi(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

/// <summary>The steps <see cref="Program.Run"/> performs; <see cref="Default"/> is what the real app uses.</summary>
internal sealed record StartupSteps(
    Action<ExceptionLog?> InstallCrashLog,
    Func<string[], InstanceClaim> ClaimInstance,
    Func<string[], int> StartUi)
{
    public static StartupSteps Default { get; } = new(
        ExceptionLog.InstallProcessHandlers,
        args => AppInstance.Claim(args),
        args => Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args));
}
