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
    private static readonly Regex HostCharacters = HostCharactersRegex();
    private static readonly Regex UnsafeUrlCharacter = UnsafeUrlCharacterRegex();

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
            host = string.Empty;
            port = defaultPort;
            return false;
        }

        host = hostToken;
        return true;
    }


    /// <summary>
    /// Result of stripping a pasted <c>set realmlist …</c> line (or multi-line paste) into a host token.
    /// </summary>
    /// <param name="Token">Host or host:port ready for <see cref="TryParseHostAddress"/>; empty when nothing usable remains.</param>
    /// <param name="IgnoredOtherLines">True when the paste had more than one non-empty line and only the realmlist line was kept.</param>
    public readonly record struct PastedAddress(string Token, bool IgnoredOtherLines);

    /// <summary>
    /// Normalizes a pasted address: trim, pick the <c>set realmlist</c> line from a multi-line paste,
    /// drop that prefix (case-insensitive), surrounding quotes and a trailing <c>;</c>.
    /// </summary>
    public static PastedAddress NormalizePastedAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new PastedAddress(string.Empty, false);

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0)
            return new PastedAddress(string.Empty, false);

        var realmlistLines = lines.Where(LooksLikeRealmlistDirective).ToArray();
        var chosen = realmlistLines.Length > 0 ? realmlistLines[0] : lines[0];
        var ignoredOther = lines.Length > 1;
        return new PastedAddress(StripRealmlistDecorations(chosen), ignoredOther);
    }

    /// <summary>
    /// Normalizes a pasted address then parses it with <see cref="TryParseHostAddress"/>.
    /// </summary>
    public static bool TryParsePastedHostAddress(
        string? text,
        out string host,
        out int port,
        out bool ignoredOtherLines,
        int defaultPort = DefaultPort)
    {
        var pasted = NormalizePastedAddress(text);
        ignoredOtherLines = pasted.IgnoredOtherLines;
        return TryParseHostAddress(pasted.Token, out host, out port, defaultPort);
    }

    private static bool LooksLikeRealmlistDirective(string line)
    {
        // "set" + whitespace + "realmlist" (any casing), so a pasted Config.wtf / website line is recognized.
        if (!line.StartsWith("set", StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = line[3..].TrimStart();
        return rest.StartsWith("realmlist", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripRealmlistDecorations(string line)
    {
        var value = line.Trim();
        if (LooksLikeRealmlistDirective(value))
        {
            // Drop "set" then "realmlist", allowing any whitespace between the words.
            value = value[3..].TrimStart();
            value = value["realmlist".Length..].TrimStart();
        }

        // Trailing ';' is common in website instructions ("set realmlist host;"). Strip it before quotes
        // so 'set realmlist "host";' still yields host.
        if (value.EndsWith(';'))
            value = value[..^1].TrimEnd();

        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1].Trim();
        }

        return value.Trim();
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
        // Check the text as written: Uri escapes characters and drops the fragment from PathAndQuery, so a value such as
        // "http://patch.example.org/#$(id)" must be refused here, before parsing.
        if (UnsafeUrlCharacter.IsMatch(value))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the patchlist URL contains forbidden characters", lineNumber);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the patchlist URL is not valid", lineNumber);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the patchlist URL must not include user information", lineNumber);

        // Validate the authority as written: Uri normalizes "http://2130706433/" to 127.0.0.1 and drops zone ids.
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = value.IndexOfAny(['/', '?'], authorityStart);
        var authority = authorityEnd < 0 ? value[authorityStart..] : value[authorityStart..authorityEnd];
        ValidateHostOrAddressLiteral(authority, lineNumber);

        if (uri.Fragment.Length > 0)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the patchlist URL must not include a fragment", lineNumber);
        var pathAndQuery = uri.PathAndQuery;
        if (pathAndQuery.Length > 0 && ShellMetacharacter.IsMatch(pathAndQuery))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the patchlist URL contains forbidden characters", lineNumber);
    }

    private static void ValidateHostOrAddressLiteral(string value, int lineNumber)
    {
        if (!TrySplitHostAndPort(value, out var hostToken, out var explicitPort))
        {
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue,
                value.StartsWith('[') ? "the IPv6 address is not valid (use [address] or [address]:port)" : "the host name is not valid",
                lineNumber);
        }

        if (explicitPort is int p && (p < 1 || p > 65535))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the port must be between 1 and 65535", lineNumber);

        if (hostToken.StartsWith('['))
        {
            ValidateIpv6Literal(hostToken.Length >= 2 && hostToken.EndsWith(']') ? hostToken[1..^1] : hostToken, lineNumber);
            return;
        }

        // Only letters, digits, '.', '-' and (for bare IPv6) ':' can appear in a host; this also refuses IPv6 zone ids ('%').
        if (!HostCharacters.IsMatch(hostToken))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the host name is not valid", lineNumber);

        if (hostToken.Contains(':'))
        {
            ValidateIpv6Literal(hostToken, lineNumber);
            return;
        }

        if (LooksNumeric(hostToken))
        {
            // IPAddress.TryParse also accepts "127.1", "2130706433", "0x7f000001" and "0177.0.0.1"; only the canonical
            // dotted-decimal form is allowed, and a host whose last label is numeric must be such an address.
            if (!IPAddress.TryParse(hostToken, out var ipv4)
                || ipv4.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
                || !string.Equals(ipv4.ToString(), hostToken, StringComparison.Ordinal))
            {
                throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue,
                    "an IPv4 address must be four decimal numbers 0-255 without leading zeros (for example 192.0.2.10)", lineNumber);
            }

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

    private static void ValidateIpv6Literal(string inner, int lineNumber)
    {
        if (inner.Length == 0 || inner.IndexOfAny(['[', ']', '%']) >= 0
            || !IPAddress.TryParse(inner, out var ip)
            || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6
            || ip.ScopeId != 0)
        {
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue,
                "the IPv6 address is not valid (zone ids such as %eth0 are not allowed)", lineNumber);
        }
    }

    /// <summary>True when the last label is all digits or a 0x hex number, i.e. the host can only be read as an IPv4 address.</summary>
    private static bool LooksNumeric(string host)
    {
        var last = host[(host.LastIndexOf('.') + 1)..];
        if (last.Length == 0)
            return host.All(c => char.IsAsciiDigit(c) || c == '.');
        return last.All(char.IsAsciiDigit)
               || (last.Length > 2 && last.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && last[2..].All(char.IsAsciiHexDigit));
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

    [GeneratedRegex(@"^[A-Za-z0-9.:\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HostCharactersRegex();

    [GeneratedRegex(@"[;$\x60|()<>&#\\'""!{}^*\s]")]
    private static partial Regex UnsafeUrlCharacterRegex();
}
