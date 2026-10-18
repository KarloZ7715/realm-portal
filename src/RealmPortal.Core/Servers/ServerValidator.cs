using System.Globalization;
using System.Text;

namespace RealmPortal.Core.Servers;

/// <summary>Shared validation for servers created by the app and servers loaded from storage.</summary>
public static class ServerValidator
{
    public static void Validate(Server server)
    {
        ArgumentNullException.ThrowIfNull(server);
        ValidateId(server.Id);
        ValidateName(server.Name);
        ValidateHost(server.Host);
        ValidateMetadata(server.Realm, Server.MaxRealmLength, "Realm");
        if (server.Account is not null)
            ValidateMetadata(server.Account, Server.MaxAccountLength, "Account");
        if (server.RequiredClientBuild is <= 0)
            throw new ArgumentException("Client build must be a positive number.", nameof(server.RequiredClientBuild));

        RealmlistText.Validate(server.Realmlist);
        var firstLineEnd = server.Realmlist.IndexOf(RealmlistText.LineEnding, StringComparison.Ordinal);
        var realmlistHost = server.Realmlist[RealmlistText.Prefix.Length..firstLineEnd];
        if (!string.Equals(server.Host, realmlistHost, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Host must match the host in the realmlist.", nameof(server.Host));
    }

    private static void ValidateId(string id)
    {
        if (id.Length != 32 || id.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Id must be exactly 32 lowercase hexadecimal characters.", nameof(Server.Id));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > Server.MaxNameLength || HasForbiddenMetadataCharacters(name))
            throw new ArgumentException($"Name must be 1-{Server.MaxNameLength} characters without control characters, quotes, backslashes or format characters.", nameof(Server.Name));
    }

    private static void ValidateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host must not be empty.", nameof(Server.Host));
    }

    private static void ValidateMetadata(string value, int maxLength, string field)
    {
        if (value.Length > maxLength || HasForbiddenMetadataCharacters(value))
            throw new ArgumentException($"{field} must be at most {maxLength} characters without control characters, quotes, backslashes or format characters.", field);
    }

    private static bool HasForbiddenMetadataCharacters(string value) =>
        value.EnumerateRunes().Any(r => r.Value is '"' or '\\' || Rune.IsControl(r) || IsFormatOrLineSeparator(r));

    private static bool IsFormatOrLineSeparator(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
    }
}
