using RealmPortal.Core.Updates;

namespace RealmPortal.App.Network;

/// <summary>App-facing install result from Core <see cref="UpdateInstallResult"/>.</summary>
public sealed record UpdateInstallOutcome(
    string SavedPath,
    InstallKind Kind,
    bool ReplacedAppImage = false,
    string? CopyableCommand = null,
    string? BackupPath = null,
    string? Instructions = null)
{
    public static UpdateInstallOutcome FromCore(UpdateInstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(
            result.SavedPath,
            result.Kind,
            result.ReplacedAppImage,
            result.CopyableCommand,
            result.BackupPath,
            result.Instructions);
    }

    /// <summary>True when Core only handed back a release-page URL (Unknown install kind).</summary>
    public bool IsReleasePageFallback =>
        Kind is InstallKind.Unknown
        && ReleasePageUrl.TryGetTrusted(SavedPath, out _);
}
