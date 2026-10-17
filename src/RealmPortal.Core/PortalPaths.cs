namespace RealmPortal.Core;

/// <summary>Where Realm Portal keeps its files. Both locations can be overridden (tests and the CLI always do).</summary>
public static class PortalPaths
{
    public const string ConfigDirVariable = "REALM_PORTAL_CONFIG_DIR";
    public const string ProjectionDirVariable = "REALM_PORTAL_PROJECTION_DIR";

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
}
