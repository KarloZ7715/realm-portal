using RealmPortal.Core.Servers;
using RealmPortal.Core.Service;

namespace RealmPortal.Core.IO;

/// <summary>
/// Keeps the previous valid version of a config store file as <c>&lt;file&gt;.bak</c> and restores it (#196).
/// Only Realm Portal's own config files use this; game client files have their own backups.
/// </summary>
public static class ConfigFileBackup
{
    /// <summary>Suffix of the copy of the previous valid version, next to the store file.</summary>
    public const string BackupSuffix = ".bak";

    /// <summary>Suffix under which a restore keeps the file it replaced, so nothing is lost.</summary>
    public const string BeforeRestoreSuffix = ".before-restore";

    /// <summary>
    /// Call right before replacing <paramref name="path"/>: copies its current content to <c>&lt;path&gt;.bak</c>
    /// (private permissions, atomic) when that content is valid. A missing, unreadable or invalid current file is not
    /// copied, so a good <c>.bak</c> is never replaced by a damaged one.
    /// </summary>
    public static void KeepPrevious(string path, IAtomicFileWriter writer, Func<byte[], bool> isValid)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(isValid);
        if (TryRead(path) is not { } current || !isValid(current))
            return;
        writer.WriteAllBytes(path + BackupSuffix, current, PrivateStorage.PrivateFilePermissions);
    }

    /// <summary>Reports whether <paramref name="path"/> is missing, valid or damaged (unreadable or invalid).</summary>
    public static ConfigFileState Inspect(string path, Func<byte[], bool> isValid)
    {
        ArgumentNullException.ThrowIfNull(isValid);
        if (!File.Exists(path))
            return ConfigFileState.Missing;
        return TryRead(path) is { } bytes && isValid(bytes) ? ConfigFileState.Valid : ConfigFileState.Damaged;
    }

    /// <summary>
    /// Replaces <paramref name="path"/> with its valid <c>.bak</c>. The replaced file, if any, is first moved to
    /// <c>&lt;path&gt;.before-restore</c> (overwriting an older one) and moved back if the write fails.
    /// The <c>.bak</c> itself is kept.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no <c>.bak</c>.</exception>
    /// <exception cref="StoreException">The <c>.bak</c> is unreadable or invalid; nothing was changed.</exception>
    public static void Restore(string path, IAtomicFileWriter writer, Func<byte[], bool> isValid)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(isValid);
        var backupPath = path + BackupSuffix;
        if (!File.Exists(backupPath))
            throw new FileNotFoundException($"There is no backup '{backupPath}'.", backupPath);
        var backup = TryRead(backupPath);
        if (backup is null || !isValid(backup))
            throw new StoreException($"'{backupPath}' is damaged; it cannot be restored.");

        var beforeRestorePath = path + BeforeRestoreSuffix;
        var moved = false;
        if (File.Exists(path))
        {
            File.Move(path, beforeRestorePath, overwrite: true);
            moved = true;
        }

        try
        {
            writer.WriteAllBytes(path, backup, PrivateStorage.PrivateFilePermissions);
        }
        catch when (moved)
        {
            try
            {
                File.Move(beforeRestorePath, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The replaced file stays at the .before-restore path.
            }

            throw;
        }
    }

    private static byte[]? TryRead(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return PrivateStorage.ReadBounded(path, PrivateStorage.MaxJsonBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or StoreException)
        {
            return null;
        }
    }
}
