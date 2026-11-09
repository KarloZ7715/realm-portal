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
        // Hidden packaging probe (#206): the release workflow runs it on every built package so a wrong
        // -p:InstallKind fails the build. Answered before the single-instance claim and the UI, so it never
        // talks to a running window or opens one.
        if (args is [PrintInstallKindFlag])
        {
            Console.Out.WriteLine(FormatInstallKind(
                RealmPortal.Core.Updates.InstallKindInfo.Current,
                RealmPortal.Core.Updates.InstallKindInfo.AppImageSelfReplace));
            Console.Out.Flush();
            return 0;
        }

        steps.InstallCrashLog(null);
        using var instance = steps.ClaimInstance(args);
        if (instance.ShouldExit)
            return 0;
        // SIGTERM (logout, `kill`) does not unwind Main: remove the socket there too. A leftover is harmless anyway.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => instance.Dispose();
        return steps.StartUi(args);
    }

    internal const string PrintInstallKindFlag = "--print-install-kind";

    /// <summary>The probe's one output line, e.g. <c>AppImage self-replace=true</c>; packaging/check-install-kind.sh parses it.</summary>
    internal static string FormatInstallKind(RealmPortal.Core.Updates.InstallKind kind, bool appImageSelfReplace) =>
        $"{kind} self-replace={(appImageSelfReplace ? "true" : "false")}";

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
