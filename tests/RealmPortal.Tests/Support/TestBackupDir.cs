using RealmPortal.Core.ClientFiles;

namespace RealmPortal.Tests.Support;

/// <summary>
/// Per-test backup folder under a <see cref="TempDirectory"/>.
/// Pass <see cref="Variables"/> as <c>getVariable</c> so PortalService, CliApp and Program.Run
/// never fall back to the user backup folder.
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

    /// <summary>Same as <see cref="Variables(TempDirectory)"/>, but other names are answered by <paramref name="inner"/>.</summary>
    public static Func<string, string?> Variables(TempDirectory temp, Func<string, string?> inner)
    {
        var backups = Folder(temp);
        return name => name == ClientFilesApplier.BackupDirectoryVariable ? backups : inner(name);
    }
}
