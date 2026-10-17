using RealmPortal.Core.Clients;

namespace RealmPortal.Core.ClientFiles;

/// <summary>
/// The complete, dry-run result of planning: exactly the edits that <see cref="IClientFilesApplier.Apply"/> will perform.
/// The UI can show a diff from it. Edits that would not change any byte are omitted.
/// </summary>
public sealed record ClientFilesPlan(
    string InstallationId,
    string ProfileId,
    string ClientRoot,
    ClientVersion Version,
    string Realmlist,
    string Realm,
    IReadOnlyList<ClientFileEdit> Edits)
{
    /// <summary>True when the client already already has this server's configuration (nothing to write, no backup is made).</summary>
    public bool IsEmpty => Edits.Count == 0;
}
