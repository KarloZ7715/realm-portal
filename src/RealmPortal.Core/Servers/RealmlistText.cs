using System.Text;

namespace RealmPortal.Core.Servers;

/// <summary>Builds and validates the canonical realmlist text.</summary>
/// <remarks>
/// Contract (the consuming launcher relies on it): the text is pure ASCII (printable characters plus the
/// <c>\r\n</c> line terminators), starts with exactly <c>set realmlist </c> in lowercase followed by the host,
/// and every line, the last one included, ends with <c>\r\n</c>.
/// </remarks>
public static class RealmlistText
{
    /// <summary>The only line terminator used in realmlist text.</summary>
    public const string LineEnding = "\r\n";

    /// <summary>The exact (lowercase) prefix of the first line.</summary>
    public const string Prefix = "set realmlist ";

    /// <summary>True for printable ASCII (0x20-0x7E).</summary>
    public static bool IsPrintableAscii(char c) => c is >= ' ' and <= '~';

    /// <summary>
    /// Returns <c>set realmlist {host}</c> followed by the extra lines, each line terminated by <c>\r\n</c>
    /// (the last one too). Extra lines are trimmed; blank ones are dropped.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The host is empty, or the host or an extra line contains a line break or a non-printable or non-ASCII character
    /// (internationalised host names must be given in their ASCII/punycode form).
    /// </exception>
    public static string Build(string host, IEnumerable<string>? extraLines = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Length == 0 || host.Any(c => !IsPrintableAscii(c)))
            throw new ArgumentException("The host must be non-empty printable ASCII without line breaks.", nameof(host));

        var text = new StringBuilder();
        text.Append(Prefix).Append(host).Append(LineEnding);

        foreach (var raw in extraLines ?? [])
        {
            var line = raw?.Trim() ?? "";
            if (line.Length == 0)
                continue;
            if (line.Any(c => !IsPrintableAscii(c)))
                throw new ArgumentException(
                    "A realmlist line must be printable ASCII without line breaks.", nameof(extraLines));
            text.Append(line).Append(LineEnding);
        }

        return text.ToString();
    }

    /// <summary>
    /// Checks the contract above for a realmlist text (for example one read from a hand-edited file).
    /// Returns <c>null</c> when valid, otherwise a short human-readable reason.
    /// </summary>
    public static string? Validate(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "the realmlist is empty";
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            return $"it must start with \"{Prefix}\" in lowercase";
        if (text.Length == Prefix.Length || text[Prefix.Length] is '\r' or '\n')
            return "the host after \"set realmlist\" is missing";
        if (!text.EndsWith(LineEnding, StringComparison.Ordinal))
            return "every line, including the last, must end with CR LF";

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 >= text.Length || text[i + 1] != '\n')
                    return "it contains a bare CR (line endings must be CR LF)";
                i++;
            }
            else if (c == '\n')
            {
                return "it contains a bare LF (line endings must be CR LF)";
            }
            else if (!IsPrintableAscii(c))
            {
                return $"it contains the non-ASCII or control character U+{(int)c:X4} (only printable ASCII is allowed)";
            }
        }

        return null;
    }
}
