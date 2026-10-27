using System.Reflection;

namespace RealmPortal.Core;

/// <summary>Static identity of the application shared by the CLI and the GUI.</summary>
public static class AppInfo
{
    public const string Name = "Realm Portal";

    /// <summary>Public source repository (opens in the system browser from About).</summary>
    public const string RepositoryUrl = "https://github.com/KarloZ7715/realm-portal";

    /// <summary>Copyright line shown in About (matches LICENSE).</summary>
    public const string Copyright = "Copyright (c) 2026 Carlos Canabal Cordero";

    public const string DisclaimerEs = "No afiliado ni respaldado por Blizzard Entertainment.";

    public const string DisclaimerEn = "Not affiliated with or endorsed by Blizzard Entertainment.";

    /// <summary>Informational version (without build metadata such as the git hash).</summary>
    public static string Version
    {
        get
        {
            var info = typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info))
                return "0.0.0";
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
    }

    /// <summary>Returns the disclaimer for a culture name such as "es", "es-CO" or "en-US" (English by default).</summary>
    public static string GetDisclaimer(string? cultureName) =>
        cultureName is not null && cultureName.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? DisclaimerEs
            : DisclaimerEn;
}
