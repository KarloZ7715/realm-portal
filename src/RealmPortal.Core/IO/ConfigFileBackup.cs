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
    /// Replaces <paramref name="path"/> with <paramref name="content"/>, first keeping its current content as
    /// <c>&lt;path&gt;.bak</c> (private permissions, atomic) when that content is valid.
    /// <list type="bullet">
    /// <item>When the current bytes already equal <paramref name="content"/>, nothing is written (neither the file nor the
    /// <c>.bak</c>), so a save without changes never replaces the real previous version.</item>
    /// <item>A missing, unreadable or invalid current file is not copied, so a good <c>.bak</c> is never replaced by a
    /// damaged one.</item>
    /// <item>The <c>.bak</c> copy is best effort: if it cannot be written (for example the <c>.bak</c> path is a
    /// directory), the save still goes ahead.</item>
    /// </list>
    /// </summary>
    /// <returns><c>false</c> when the content was unchanged and nothing was written.</returns>
    public static bool WriteKeepingPrevious(
        string path, IAtomicFileWriter writer, byte[] content, FileReplacementPermissions? permissions, Func<byte[], bool> isValid)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(isValid);
        var current = TryRead(path);
        if (current is not null)
        {
            if (current.AsSpan().SequenceEqual(content))
                return false;
            if (isValid(current))
                TryKeep(path + BackupSuffix, writer, current);
        }

        writer.WriteAllBytes(path, content, permissions);
        return true;
    }

    /// <summary>
    /// Reports whether <paramref name="path"/> is missing, valid or damaged (unreadable, invalid, or not a regular file,
    /// such as a directory).
    /// </summary>
    public static ConfigFileState Inspect(string path, Func<byte[], bool> isValid)
    {
        ArgumentNullException.ThrowIfNull(isValid);
        if (Directory.Exists(path))
            return ConfigFileState.Damaged;
        if (!File.Exists(path))
            return ConfigFileState.Missing;
        return TryRead(path) is { } bytes && isValid(bytes) ? ConfigFileState.Valid : ConfigFileState.Damaged;
    }

    /// <summary>
    /// Replaces <paramref name="path"/> with its valid <c>.bak</c>. The current file, if any, is first copied to
    /// <c>&lt;path&gt;.before-restore</c> (private permissions, overwriting an older one), then <paramref name="path"/> is
    /// replaced atomically, so an unexpected exit never leaves the store file missing. When the current file already
    /// equals the <c>.bak</c>, nothing is changed (an earlier <c>.before-restore</c> is kept). If the write fails, the
    /// current file is left as it was and the new <c>.before-restore</c> copy is removed. The <c>.bak</c> itself is kept.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no <c>.bak</c>.</exception>
    /// <exception cref="InvalidDataException">The <c>.bak</c> is unreadable or invalid; nothing was changed.</exception>
    public static void Restore(string path, IAtomicFileWriter writer, Func<byte[], bool> isValid)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(isValid);
        var backupPath = path + BackupSuffix;
        if (!File.Exists(backupPath) && !Directory.Exists(backupPath))
            throw new FileNotFoundException($"There is no backup '{backupPath}'.", backupPath);
        var backup = TryRead(backupPath);
        if (backup is null || !isValid(backup))
            throw new InvalidDataException($"'{backupPath}' is damaged; it cannot be restored.");

        var beforeRestorePath = path + BeforeRestoreSuffix;
        var copied = false;
        var moved = false;
        if (File.Exists(path))
        {
            if (TryRead(path) is { } current)
            {
                if (current.AsSpan().SequenceEqual(backup))
                    return;
                writer.WriteAllBytes(beforeRestorePath, current, PrivateStorage.PrivateFilePermissions);
                copied = true;
            }
            else
            {
                // The current file cannot be read (no read permission, too large): it cannot be copied, so it is
                // moved aside instead and moved back if the write fails.
                File.Move(path, beforeRestorePath, overwrite: true);
                moved = true;
                MakePrivate(beforeRestorePath);
            }
        }

        try
        {
            writer.WriteAllBytes(path, backup, PrivateStorage.PrivateFilePermissions);
        }
        catch
        {
            try
            {
                if (moved)
                    File.Move(beforeRestorePath, path, overwrite: true);
                else if (copied)
                    File.Delete(beforeRestorePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The replaced file stays at the .before-restore path.
            }

            throw;
        }
    }

    private static void TryKeep(string backupPath, IAtomicFileWriter writer, byte[] current)
    {
        try
        {
            writer.WriteAllBytes(backupPath, current, PrivateStorage.PrivateFilePermissions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a .bak that cannot be written must not block the save itself. Inspect reports the
            // .bak as Damaged (for example a directory) or Missing.
        }
    }

    private static void MakePrivate(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            File.SetUnixFileMode(path, PrivateStorage.PrivateFileMode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep going: the restore itself matters more than the mode of the kept copy.
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
