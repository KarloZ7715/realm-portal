using RealmPortal.App.Localization;
using RealmPortal.Core.Updates;

namespace RealmPortal.App.Network;

/// <summary>
/// Builds user-visible install follow-up text from <see cref="UpdateInstallOutcome"/>.
/// Kind-specific copy comes from Loc (en/es); paths and shell commands stay as data.
/// </summary>
public static class UpdateInstallPresentation
{
    /// <summary>
    /// Localized detail for the update-ready dialog / Settings soft status.
    /// Prefer copyable command + path when present; always include <see cref="UpdateInstallOutcome.SavedPath"/>
    /// alongside instructions when both exist (e.g. Windows ZIP).
    /// </summary>
    public static string? FormatDetail(UpdateInstallOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.IsReleasePageFallback)
            return Loc.Get("update.install.open_release");

        return outcome.Kind switch
        {
            InstallKind.AppImage when outcome.ReplacedAppImage =>
                Loc.Format("update.install.appimage.restart", outcome.SavedPath),

            InstallKind.AppImage when !string.IsNullOrWhiteSpace(outcome.CopyableCommand) =>
                Loc.Format("update.install.appimage.chmod", outcome.SavedPath, outcome.CopyableCommand),

            InstallKind.Deb when !string.IsNullOrWhiteSpace(outcome.CopyableCommand) =>
                Loc.Format("update.install.deb", outcome.SavedPath, outcome.CopyableCommand),

            InstallKind.WindowsZip =>
                Loc.Format("update.install.windows", outcome.SavedPath),

            _ when !string.IsNullOrWhiteSpace(outcome.CopyableCommand)
                   && !string.IsNullOrWhiteSpace(outcome.SavedPath) =>
                Loc.Format("update.install.command_and_path", outcome.SavedPath, outcome.CopyableCommand),

            _ when !string.IsNullOrWhiteSpace(outcome.SavedPath)
                   && !string.IsNullOrWhiteSpace(outcome.Instructions) =>
                // Fallback when kind is unexpected but Core returned both path + English instructions:
                // still show the path; do not paste Core English — use a neutral saved-file line.
                Loc.Format("update.install.saved", outcome.SavedPath),

            _ when !string.IsNullOrWhiteSpace(outcome.SavedPath) =>
                Loc.Format("update.install.saved", outcome.SavedPath),

            _ => null,
        };
    }
}
