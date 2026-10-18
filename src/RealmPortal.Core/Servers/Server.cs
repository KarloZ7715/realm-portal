namespace RealmPortal.Core.Servers;

/// <summary>A game server added manually by the user. Immutable; build it with <see cref="Create"/>.</summary>
/// <param name="Id">Stable identifier (32 lowercase hex digits). It is the <c>server_id</c> of <c>activo.json</c>.</param>
/// <param name="Name">Display name chosen by the user.</param>
/// <param name="Host">Address used in the realmlist, for example <c>logon.example.org</c> or <c>10.0.0.5:3724</c>.</param>
/// <param name="Realm">Realm name (may be empty).</param>
/// <param name="Realmlist">Full realmlist text: lines separated and terminated by <c>\r\n</c>, including extra lines such as <c>set patchlist</c>.</param>
/// <param name="RequiredClientBuild">Client build number the server needs (for example <c>12340</c>), or <c>null</c> when any client works. Compared with <c>ClientVersion.Build</c>.</param>
/// <param name="Account">Remembered account name (never a password), or <c>null</c>.</param>
/// <param name="ClientId">Preferred client id when the server was added; <c>null</c> when none.</param>
public sealed record Server(
    string Id,
    string Name,
    string Host,
    string Realm,
    string Realmlist,
    int? RequiredClientBuild,
    string? Account,
    string? ClientId = null)
{
    public const int MaxNameLength = 64;
    public const int MaxRealmLength = 64;
    public const int MaxAccountLength = 64;

    /// <summary>Creates a validated server with a freshly generated id (or <paramref name="id"/> when given).</summary>
    /// <param name="extraRealmlistLines">Extra realmlist lines after <c>set realmlist</c>, such as <c>set patchlist patch.example.org</c>.</param>
    /// <exception cref="ArgumentException">The name, realm, account or id is empty, too long or contains forbidden characters.</exception>
    /// <exception cref="InvalidRealmlistException">The host or an extra line breaks the realmlist contract (see <see cref="RealmlistText"/>); no server is created.</exception>
    public static Server Create(
        string name,
        string host,
        string? realm = null,
        IEnumerable<string>? extraRealmlistLines = null,
        int? requiredClientBuild = null,
        string? account = null,
        string? id = null,
        string? clientId = null)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > MaxNameLength || HasForbiddenMetadataCharacters(name))
            throw new ArgumentException($"Name must be 1-{MaxNameLength} characters without control characters, quotes, backslashes or format characters.", nameof(name));

        host = host?.Trim() ?? "";
        if (host.Length == 0)
            throw new ArgumentException("Host must not be empty.", nameof(host));

        realm = realm?.Trim() ?? "";
        if (realm.Length > MaxRealmLength || HasForbiddenMetadataCharacters(realm))
            throw new ArgumentException($"Realm must be at most {MaxRealmLength} characters without quotes, backslashes or format characters.", nameof(realm));

        account = string.IsNullOrWhiteSpace(account) ? null : account.Trim();
        if (account is not null && (account.Length > MaxAccountLength || HasForbiddenMetadataCharacters(account)))
            throw new ArgumentException($"Account must be at most {MaxAccountLength} characters without quotes, backslashes or format characters.", nameof(account));

        if (requiredClientBuild is <= 0)
            throw new ArgumentException("Client build must be a positive number.", nameof(requiredClientBuild));

        id ??= Guid.NewGuid().ToString("N");
        if (id.Length != 32 || id.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Id must be exactly 32 lowercase hexadecimal characters.", nameof(id));

        return new Server(id, name, host, realm, RealmlistText.Build(host, extraRealmlistLines), requiredClientBuild, account, clientId);
    }

    private static bool HasForbiddenMetadataCharacters(string value) =>
        value.Any(c => c is '"' or '\\' || char.IsControl(c) || IsFormatOrLineSeparator(c));

    private static bool IsFormatOrLineSeparator(char c)
    {
        var category = char.GetUnicodeCategory(c);
        return category is System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.LineSeparator
            or System.Globalization.UnicodeCategory.ParagraphSeparator;
    }
}
