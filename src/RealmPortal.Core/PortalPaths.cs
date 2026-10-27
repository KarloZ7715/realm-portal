namespace RealmPortal.Core;

/// <summary>Where Realm Portal keeps its files. Both locations can be overridden (tests and the CLI always do).</summary>
public static class PortalPaths
{
    public const string ConfigDirVariable = "REALM_PORTAL_CONFIG_DIR";
    public const string ProjectionDirVariable = "REALM_PORTAL_PROJECTION_DIR";
    public const string StateDirVariable = "REALM_PORTAL_STATE_DIR";

    /// <summary>
    /// The app's own config directory: <c>$REALM_PORTAL_CONFIG_DIR</c>, else <c>realm-portal</c> under the platform
    /// application-data folder (<c>$XDG_CONFIG_HOME</c> or <c>~/.config</c> on Linux, <c>%APPDATA%</c> on Windows).
    /// </summary>
    public static string ConfigDirectory(Func<string, string?>? getVariable = null)
    {
        var overridden = (getVariable ?? Environment.GetEnvironmentVariable)(ConfigDirVariable);
        return !string.IsNullOrWhiteSpace(overridden)
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "realm-portal");
    }

    /// <summary>
    /// Where <c>activo.json</c> is written: <c>$REALM_PORTAL_PROJECTION_DIR</c>, else <c>~/.config/wow-servidores</c>
    /// (the same relative location on every platform).
    /// </summary>
    public static string ProjectionDirectory(Func<string, string?>? getVariable = null)
    {
        var overridden = (getVariable ?? Environment.GetEnvironmentVariable)(ProjectionDirVariable);
        return !string.IsNullOrWhiteSpace(overridden)
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "wow-servidores");
    }

    /// <summary>
    /// Writable state root for logs: <c>$REALM_PORTAL_STATE_DIR</c>, else
    /// <c>$XDG_STATE_HOME/realm-portal</c> (default <c>~/.local/state/realm-portal</c>) on Unix, or
    /// <c>%LOCALAPPDATA%\RealmPortal</c> on Windows.
    /// </summary>
    public static string StateDirectory(Func<string, string?>? getVariable = null)
    {
        var get = getVariable ?? Environment.GetEnvironmentVariable;
        var overridden = get(StateDirVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
            return overridden;

        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RealmPortal");

        var xdgState = get("XDG_STATE_HOME");
        var stateHome = !string.IsNullOrWhiteSpace(xdgState)
            ? xdgState
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(stateHome, "realm-portal");
    }

    /// <summary>Directory that holds <see cref="ExceptionLog.FileName"/> (<c>…/logs</c> under <see cref="StateDirectory"/>).</summary>
    public static string LogDirectory(Func<string, string?>? getVariable = null) =>
        Path.Combine(StateDirectory(getVariable), "logs");

    /// <summary>Full path of the crash / exception log file.</summary>
    public static string CrashLogFile(Func<string, string?>? getVariable = null) =>
        Path.Combine(LogDirectory(getVariable), ExceptionLog.FileName);
}
