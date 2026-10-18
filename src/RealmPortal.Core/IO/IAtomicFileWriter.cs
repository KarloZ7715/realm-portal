namespace RealmPortal.Core.IO;

/// <summary>Replaces a file so that readers see either the old content or the new one, never a partial write.</summary>
public interface IAtomicFileWriter
{
    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/>, creating missing parent directories.
    /// On failure the previous content of <paramref name="path"/> (if any) is left untouched.
    /// </summary>
    void WriteAllBytes(string path, byte[] content);

    /// <summary>
    /// Like <see cref="WriteAllBytes(string, byte[])"/> but copies permission bits onto the temp file (Unix) and clears read-only
    /// on the destination (Windows) before the atomic rename.
    /// </summary>
    void WriteAllBytes(string path, byte[] content, FileReplacementPermissions? permissions);
}
