namespace RealmPortal.Core.Clients;

/// <summary>
/// A client folder found by <see cref="ClientScanner"/>. Discovery is read-only: nothing is registered and
/// nothing is written into the candidate folder.
/// </summary>
/// <param name="Id">Stable id for the canonical folder path (<see cref="ClientIds.FromFolderPath"/>); the same id <c>ClientStore</c> uses.</param>
/// <param name="Folder">Canonical client folder (the one that holds <c>Wow.exe</c>).</param>
/// <param name="Version">The version read from the executable, or <c>null</c> when it could not be read.</param>
/// <param name="VersionError">Why the version could not be read; <c>null</c> when <paramref name="Version"/> is set.</param>
/// <param name="Origin">Where the folder was found, with the highest-precedence source when several point at it.</param>
public sealed record DiscoveredClient(
    string Id,
    string Folder,
    ClientVersion? Version,
    ClientVersionErrorKind? VersionError,
    ClientOrigin Origin)
{
    /// <summary>The client build number, or <c>null</c> when the version could not be read.</summary>
    public int? Build => Version?.Build;
}
