using RealmPortal.Core.ClientFiles;

namespace RealmPortal.App.Tests.Support;

/// <summary>
/// Per-test backup folder under a <see cref="TempDirectory"/>.
/// Pass <see cref="Variables"/> as <c>getVariable</c> when a view model or session constructs a real PortalService.
/// </summary>
public static class TestBackupDir
{
    public static string Folder(TempDirectory temp)
    {
        var path = temp.Combine("backups");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Answers <c>REALM_PORTAL_BACKUP_DIR</c> with <see cref="Folder"/> and null for every other name.</summary>
    public static Func<string, string?> Variables(TempDirectory temp)
    {
        var backups = Folder(temp);
        return name => name == ClientFilesApplier.BackupDirectoryVariable ? backups : null;
    }
}
