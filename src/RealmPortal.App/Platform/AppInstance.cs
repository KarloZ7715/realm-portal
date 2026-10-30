using System.Runtime.InteropServices;
using RealmPortal.Core;
using RealmPortal.Core.Instances;

namespace RealmPortal.App.Platform;

/// <summary>
/// Startup glue for the single-instance guard (#197): one app window per config directory. A second launch asks the
/// running one to come to the front and exits. See docs/single-instance.md.
/// </summary>
internal static class AppInstance
{
    /// <summary>Instance name of the desktop app inside the config directory.</summary>
    public const string Name = "app";

    /// <summary>
    /// Claims the instance for <paramref name="args"/>. Developer modes (<c>--gallery</c>, <c>--demo</c>) use no real
    /// data and skip the guard. Never throws: any failure starts the app normally.
    /// </summary>
    public static InstanceClaim Claim(string[] args, Func<string, string?>? getVariable = null, TextWriter? log = null)
    {
        if (args.Contains("--gallery") || args.Contains("--demo"))
            return InstanceClaim.None;

        log ??= Console.Error;
        InstanceClaim claim;
        try
        {
            var endpoint = InstanceEndpoint.For(PortalPaths.ConfigDirectory(getVariable), Name, getVariable);
            claim = SingleInstance.Claim(endpoint, InstanceMessage.Focus, InstanceActivation.Handle,
                new SingleInstanceOptions { BeforeForward = AllowForeground });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            log.WriteLine($"Realm Portal: single-instance check skipped: {OneLine(ex.Message)}");
            return InstanceClaim.None;
        }

        switch (claim.Outcome)
        {
            case ClaimOutcome.PrimaryUnresponsive:
                // Defined behavior: start anyway. A hung first window must not lock the user out; the config-directory
                // locks (#39) keep the data consistent between the two processes.
                log.WriteLine($"Realm Portal: another instance is running but did not respond ({OneLine(claim.Detail)}); starting a second window.");
                break;
            case ClaimOutcome.Unavailable:
            case ClaimOutcome.Primary when claim.Detail is not null:
                log.WriteLine($"Realm Portal: single-instance check incomplete: {OneLine(claim.Detail)}");
                break;
        }
        return claim;
    }

    /// <summary>Keeps a diagnostic on one line (paths and OS messages may contain control characters).</summary>
    internal static string OneLine(string? text) =>
        string.Concat((text ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c));

    /// <summary>
    /// Windows only lets the foreground process hand over the foreground. This launch was just started by the user, so
    /// it allows the running instance to call <c>SetForegroundWindow</c> when it receives the focus request.
    /// </summary>
    private static void AllowForeground(int processId)
    {
        if (OperatingSystem.IsWindows())
            _ = AllowSetForegroundWindow(processId);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
