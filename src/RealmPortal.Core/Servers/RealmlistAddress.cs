using System.Globalization;
using System.Text.RegularExpressions;

namespace RealmPortal.Core.Servers;

/// <summary>Strict hostname / address rules shared by Core (and mirrored by the App form).</summary>
internal static partial class RealmlistAddress
{
    public const int MaxHostLength = 253;
    public const int MaxLabelLength = 63;
    public const int MaxRealmlistBytes = 1024;

    private static readonly Regex HostnamePattern = HostnameRegex();
    private static readonly Regex Ipv6BracketPattern = Ipv6BracketRegex();
    private static readonly Regex PatchlistUrlPattern = PatchlistUrlRegex();

    /// <summary>Validates a <c>set realmlist</c> host token (not a full URL).</summary>
    public static void ValidateRealmlistHost(string value, int lineNumber)
    {
        if (value.Length == 0)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the value is missing", lineNumber);
        var bad = value.FirstOrDefault(c => c > '\u007F');
        if (bad != '\0')
            throw new InvalidRealmlistException(
                InvalidRealmlistKind.NotAscii,
                $"it contains the non-ASCII character U+{(int)bad:X4}; use ASCII only (punycode for international host names)",
                lineNumber);
        if (value.Contains("://", StringComparison.Ordinal))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the host must not include a URL scheme", lineNumber);
        ValidateHostOrBracketedIpv6(value, lineNumber);
    }

    /// <summary>Validates a <c>set patchlist</c> value: hostname (optional port) or <c>http(s)://</c> URL.</summary>
    public static void ValidatePatchlistValue(string value, int lineNumber)
    {
        if (value.Length == 0)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the value is missing", lineNumber);
        var bad = value.FirstOrDefault(c => c > '\u007F');
        if (bad != '\0')
            throw new InvalidRealmlistException(
                InvalidRealmlistKind.NotAscii,
                $"it contains the non-ASCII character U+{(int)bad:X4}; use ASCII only (punycode for international host names)",
                lineNumber);
        if (PatchlistUrlPattern.IsMatch(value))
        {
            if (value.Length > MaxHostLength + 256)
                throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the patchlist URL is too long", lineNumber);
            return;
        }

        ValidateHostOrBracketedIpv6(value, lineNumber);
    }

    private static void ValidateHostOrBracketedIpv6(string value, int lineNumber)
    {
        if (value.StartsWith('['))
        {
            if (!Ipv6BracketPattern.IsMatch(value))
                throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the IPv6 address is not valid", lineNumber);
            ValidatePort(value, lineNumber);
            return;
        }

        if (!HostnamePattern.IsMatch(value))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the host name is not valid", lineNumber);
        if (value.Contains("..", StringComparison.Ordinal))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the host name must not contain \"..\"", lineNumber);

        var hostOnly = value;
        var colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
            hostOnly = value[..colon];

        if (hostOnly.Length > MaxHostLength)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, $"the host name must be at most {MaxHostLength} characters", lineNumber);
        foreach (var label in hostOnly.Split('.'))
        {
            if (label.Length == 0 || label.Length > MaxLabelLength)
                throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "each host label must be 1-63 characters", lineNumber);
        }

        ValidatePort(value, lineNumber);
    }

    private static void ValidatePort(string value, int lineNumber)
    {
        var colon = value.LastIndexOf(':');
        if (colon < 0 || value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close >= 0 && close + 1 < value.Length)
            {
                if (value[close + 1] != ':')
                    throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the port must follow the closing bracket", lineNumber);
                colon = close + 1;
            }
            else
            {
                return;
            }
        }

        if (colon == value.Length - 1)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the port is missing", lineNumber);
        if (!int.TryParse(value[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the port must be between 1 and 65535", lineNumber);
    }

    [GeneratedRegex(@"^(?<host>[A-Za-z0-9](?:[A-Za-z0-9.\-]*[A-Za-z0-9])?)(?::(?<port>\d{1,5}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostnameRegex();

    [GeneratedRegex(@"^\[(?<addr>[0-9A-Fa-f:.]+)\](?::(?<port>\d{1,5}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6BracketRegex();

    [GeneratedRegex(@"^https?://[\x21-\x7E]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PatchlistUrlRegex();
}
