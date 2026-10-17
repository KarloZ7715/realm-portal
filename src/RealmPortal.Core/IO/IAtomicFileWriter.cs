namespace RealmPortal.Core.IO;

/// <summary>Replaces a file so that readers see either the old content or the new one, never a partial write.</summary>
public interface IAtomicFileWriter
{
    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/>, creating missing parent directories.
    /// On failure the previous content of <paramref name="path"/> (if any) is left untouched.
    /// </summary>
    void WriteAllBytes(string path, byte[] content);
}
