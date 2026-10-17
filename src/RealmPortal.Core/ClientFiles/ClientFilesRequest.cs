using System.Text.RegularExpressions;
using RealmPortal.Core.Clients;

namespace RealmPortal.Core.ClientFiles;

/// <summary>What to write into a client: the realm configuration of one server profile.</summary>
/// <param name="InstallationId">Stable id of the client installation; groups its backups. Letters, digits, '.', '_' and '-' only.</param>
/// <param name="ProfileId">Id of the server profile being applied (recorded in the backup manifest). Same character rules.</param>
/// <param name="ClientRoot">The client directory (the one that contains <c>Wow.exe</c>).</param>
/// <param name="Version">The client version; its major number selects the file layout.</param>
/// <param name="Realmlist">
/// The server's realmlist text, the single source of truth that is written as is (nothing is invented). ASCII only,
/// lines <c>set &lt;key&gt; &lt;value&gt;</c> separated by CR LF or LF, key in {realmlist, patchlist} (plus portal for
/// WoD/Legion), first line exactly <c>set realmlist &lt;host&gt;</c> in lowercase, values without quotes or spaces.
/// Anything else fails with <see cref="ClientFilesErrorCode.InvalidRealmlist"/> and nothing is written.
/// </param>
/// <param name="Realm">The realm name to preselect in the login screen; empty when the server has none.</param>
public sealed record ClientFilesRequest(
    string InstallationId,
    string ProfileId,
    string ClientRoot,
    ClientVersion Version,
    string Realmlist,
    string Realm)
{
    private static readonly Regex SafeId = new("^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant);

    /// <summary>True when the id can be used as a folder name and manifest value.</summary>
    public static bool IsValidId(string? id) => id is not null && id != "." && id != ".." && SafeId.IsMatch(id);

    /// <summary>Throws <see cref="ArgumentException"/> when the ids are unusable (a programming error, not a user error).</summary>
    public void ValidateIds()
    {
        if (!IsValidId(InstallationId))
            throw new ArgumentException("InstallationId must be 1-64 characters of letters, digits, '.', '_' or '-'.", nameof(InstallationId));
        if (!IsValidId(ProfileId))
            throw new ArgumentException("ProfileId must be 1-64 characters of letters, digits, '.', '_' or '-'.", nameof(ProfileId));
    }
}
