namespace RealmPortal.Core.ClientFiles;

public enum ClientEditKind
{
    /// <summary>The file does not exist yet and will be created.</summary>
    Create,

    /// <summary>The file exists and its content will be replaced.</summary>
    Replace,
}

/// <summary>One planned change to a single file inside the client directory.</summary>
/// <param name="RelativePath">
/// Path relative to the client root, always with <c>/</c> separators and the real on-disk casing of
/// every existing segment (so it is correct on case-sensitive file systems).
/// </param>
/// <param name="Kind">Whether the file is created or replaced.</param>
/// <param name="NewBytes">The complete new content of the file.</param>
/// <param name="Sha256Before">Lowercase hex SHA-256 of the current content; <c>null</c> for <see cref="ClientEditKind.Create"/>.</param>
/// <param name="Sha256After">Lowercase hex SHA-256 of <paramref name="NewBytes"/>.</param>
public sealed record ClientFileEdit(
    string RelativePath,
    ClientEditKind Kind,
    byte[] NewBytes,
    string? Sha256Before,
    string Sha256After);
