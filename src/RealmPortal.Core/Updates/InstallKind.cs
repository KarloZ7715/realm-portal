namespace RealmPortal.Core.Updates;

/// <summary>
/// How this build was packaged. Set at build time by packaging; never inferred from install paths at runtime.
/// Development builds are <see cref="Unknown"/>.
/// </summary>
public enum InstallKind
{
    Unknown = 0,
    AppImage = 1,
    Deb = 2,
    WindowsZip = 3,
}

/// <summary>Build-time install kind and AppImage self-replace gate (see docs/updates-and-status.md).</summary>
public static class InstallKindInfo
{
    /// <summary>
    /// Current package kind. Overridden with <c>-p:InstallKind=AppImage|Deb|WindowsZip</c>.
    /// </summary>
    public static InstallKind Current { get; } =
#if REALM_PORTAL_INSTALL_APPIMAGE
        InstallKind.AppImage;
#elif REALM_PORTAL_INSTALL_DEB
        InstallKind.Deb;
#elif REALM_PORTAL_INSTALL_WINDOWSZIP
        InstallKind.WindowsZip;
#else
        InstallKind.Unknown;
#endif

    /// <summary>
    /// When true, a verified AppImage may replace <c>$APPIMAGE</c> in place. Packaging sets
    /// <c>AppImageSelfReplace=true</c> only when GitHub immutable releases are enabled.
    /// </summary>
    public static bool AppImageSelfReplace { get; } =
#if REALM_PORTAL_APPIMAGE_SELF_REPLACE
        true;
#else
        false;
#endif
}
