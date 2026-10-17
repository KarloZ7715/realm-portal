using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace RealmPortal.Core.Servers;

/// <summary>Strict hostname / address rules shared by Core and the App add-server form.</summary>
public static partial class RealmlistAddress
{
    public const int DefaultPort = 3724;
    public const int MaxHostLength = 253;
    public const int MaxLabelLength = 63;
    public const int MaxRealmlistBytes = 1024;
    private static readonly Regex HostnamePattern = HostnameRegex();
    private static readonly Regex ShellMetacharacter = ShellMetacharacterRegex();

    /// <summary>Parses a UI address (<c>host</c> or <c>host:port</c>, bracketed IPv6 optional).</summary>
    public static bool TryParseHostAddress(string? text, out string host, out int port, int defaultPort = DefaultPort)
    {
        host = string.Empty;
        port = defaultPort;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return false;

        if (!TrySplitHostAndPort(value, out var hostToken, out var explicitPort))
            return false;

        port = explicitPort ?? defaultPort;
        var validateToken = explicitPort is null ? hostToken : $"{hostToken}:{port}";
        try
        {
            ValidateRealmlistHost(validateToken, 1);
        }
        catch (InvalidRealmlistException)
        {
            return false;
        }

        host = hostToken;
        return true;
    }

    /// <summary>Validates a <c>set realmlist</c> host token (not a full URL).</summary>
    public static void ValidateRealmlistHost(string value, int lineNumber)
    {
        if (value.Length == 0)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the value is missing", lineNumber);
        RejectNonAscii(value, lineNumber);
        if (value.Contains("://", StringComparison.Ordinal))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the host must not include a URL scheme", lineNumber);
        ValidateHostOrAddressLiteral(value, lineNumber);
    }

    /// <summary>Validates a <c>set patchlist</c> value: hostname (optional port) or <c>http(s)://</c> URL.</summary>
    public static void ValidatePatchlistValue(string value, int lineNumber)
    {
        if (value.Length == 0)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the value is missing", lineNumber);
        RejectNonAscii(value, lineNumber);
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            ValidatePatchlistUrl(value, lineNumber);
            return;
        }

        ValidateHostOrAddressLiteral(value, lineNumber);
    }

    private static void ValidatePatchlistUrl(string value, int lineNumber)
    {
        if (value.Length > MaxHostLength + 512)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the patchlist URL is too long", lineNumber);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the patchlist URL is not valid", lineNumber);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the patchlist URL must not include user information", lineNumber);

        var hostPart = uri.Host;
        if (uri.HostNameType == UriHostNameType.IPv6)
            hostPart = $"[{uri.Host}]";
        if (uri is { IsDefaultPort: false, Port: var p })
            ValidateHostOrAddressLiteral($"{hostPart}:{p}", lineNumber);
        else
            ValidateHostOrAddressLiteral(hostPart, lineNumber);

        var pathAndQuery = uri.PathAndQuery;
        if (pathAndQuery.Length > 0 && ShellMetacharacter.IsMatch(pathAndQuery))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the patchlist URL contains forbidden characters", lineNumber);
    }

    private static void ValidateHostOrAddressLiteral(string value, int lineNumber)
    {
        if (!TrySplitHostAndPort(value, out var hostToken, out var explicitPort))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the host name is not valid", lineNumber);

        if (explicitPort is int p && (p < 1 || p > 65535))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the port must be between 1 and 65535", lineNumber);

        if (hostToken.StartsWith('['))
        {
            if (!hostToken.EndsWith(']') || hostToken.Length < 3)
                throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the IPv6 address is not valid", lineNumber);
            var inner = hostToken[1..^1];
            if (!IPAddress.TryParse(inner, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the IPv6 address is not valid", lineNumber);
            return;
        }

        if (IPAddress.TryParse(hostToken, out var ipLiteral))
        {
            if (ipLiteral.AddressFamily is not (System.Net.Sockets.AddressFamily.InterNetwork
                or System.Net.Sockets.AddressFamily.InterNetworkV6))
                throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the host address is not valid", lineNumber);
            return;
        }

        if (!HostnamePattern.IsMatch(hostToken))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the host name is not valid", lineNumber);
        if (hostToken.Contains("..", StringComparison.Ordinal))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the host name must not contain \"..\"", lineNumber);
        if (hostToken.Length > MaxHostLength)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, $"the host name must be at most {MaxHostLength} characters", lineNumber);
        foreach (var label in hostToken.Split('.'))
        {
            if (label.Length == 0 || label.Length > MaxLabelLength)
                throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "each host label must be 1-63 characters", lineNumber);
        }
    }

    private static bool TrySplitHostAndPort(string value, out string hostToken, out int? explicitPort)
    {
        hostToken = value;
        explicitPort = null;
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close < 0)
                return false;
            hostToken = value[..(close + 1)];
            if (close + 1 == value.Length)
                return true;
            if (value[close + 1] != ':')
                return false;
            if (!int.TryParse(value[(close + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port))
                return false;
            explicitPort = port;
            return true;
        }

        var colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
        {
            if (!int.TryParse(value[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port))
                return false;
            explicitPort = port;
            hostToken = value[..colon];
        }

        return hostToken.Length > 0;
    }

    private static void RejectNonAscii(string value, int lineNumber)
    {
        var bad = value.FirstOrDefault(c => c > '\u007F');
        if (bad != '\0')
            throw new InvalidRealmlistException(
                InvalidRealmlistKind.NotAscii,
                $"it contains the non-ASCII character U+{(int)bad:X4}; use ASCII only (punycode for international host names)",
                lineNumber);
    }

    [GeneratedRegex(@"^(?<host>[A-Za-z0-9](?:[A-Za-z0-9.\-]*[A-Za-z0-9])?)$", RegexOptions.CultureInvariant)]
    private static partial Regex HostnameRegex();

    [GeneratedRegex(@"[;$\x60|()<>]")]
    private static partial Regex ShellMetacharacterRegex();
}
