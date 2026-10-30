using System.Text;

namespace RealmPortal.Core.Servers;

/// <summary>Builds and validates the realmlist text of a server.</summary>
/// <remarks>
/// The realmlist text is the source of truth and is written as-is. Contract (the consuming launcher relies on it):
/// <list type="bullet">
/// <item>pure ASCII; every line, the last one included, ends with <c>\r\n</c> (no bare CR or LF);</item>
/// <item>every line has the form <c>set &lt;key&gt; &lt;value&gt;</c> (lowercase <c>set</c>, single spaces) with key
/// <c>realmlist</c> or <c>patchlist</c>, each at most once. Other keys are rejected; <c>portal</c> is only meaningful
/// for WoD/Legion clients, which the project does not model yet, so it is rejected for now;</item>
/// <item>the first line is <c>set realmlist &lt;host&gt;</c> in lowercase;</item>
/// <item>values contain no quotes (<c>"</c> or <c>'</c>), spaces or control characters.</item>
/// </list>
/// </remarks>
public static class RealmlistText
{
    /// <summary>The only line terminator used in realmlist text.</summary>
    public const string LineEnding = "\r\n";

    /// <summary>The exact (lowercase) prefix of the first line.</summary>
    public const string Prefix = "set realmlist ";

    private static readonly string[] AllowedKeys = ["realmlist", "patchlist"];

    /// <summary>
    /// Returns <c>set realmlist {host}</c> followed by the extra lines, each line terminated by <c>\r\n</c>
    /// (the last one too). Extra lines are trimmed; blank ones are dropped. The result is validated.
    /// </summary>
    /// <exception cref="InvalidRealmlistException">The resulting text breaks the contract; nothing is returned. When an extra
    /// line caused it, <see cref="InvalidRealmlistException.ExtraLineIndex"/> is its index in <paramref name="extraLines"/>
    /// (an extra line that still contains a line break after trimming is rejected as <see cref="InvalidRealmlistKind.BadLineEnding"/>).</exception>
    public static string Build(string host, IEnumerable<string>? extraLines = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Contains('\r') || host.Contains('\n'))
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, "the host must not contain line breaks", 1);

        var text = new StringBuilder();
        text.Append(Prefix).Append(host).Append(LineEnding);
        var sourceIndexes = new List<int>(); // sourceIndexes[n - 2] = index in extraLines of realmlist line n
        var index = -1;
        foreach (var raw in extraLines ?? [])
        {
            index++;
            var line = raw?.Trim() ?? "";
            if (line.Length == 0)
                continue;
            if (line.Contains('\r') || line.Contains('\n'))
                throw new InvalidRealmlistException(
                    InvalidRealmlistKind.BadLineEnding, "an extra line must not contain line breaks", sourceIndexes.Count + 2,
                    extraLineIndex: index);
            text.Append(line).Append(LineEnding);
            sourceIndexes.Add(index);
        }

        RealmlistAddress.ValidateRealmlistHost(host, 1);

        var result = text.ToString();
        try
        {
            Validate(result);
        }
        catch (InvalidRealmlistException ex) when (ex.LineNumber is { } number && number >= 2 && number - 2 < sourceIndexes.Count)
        {
            throw new InvalidRealmlistException(ex.Kind, ex.Reason, ex.LineNumber, ex.ServerName, sourceIndexes[number - 2]);
        }

        return result;
    }

    /// <summary>
    /// The lines of a stored realmlist after the first <c>set realmlist</c> line (for example <c>set patchlist …</c>),
    /// without line endings. Passing them back to <see cref="Build"/> with the same host rebuilds the same text.
    /// </summary>
    public static IReadOnlyList<string> ExtraLines(string realmlist)
    {
        ArgumentNullException.ThrowIfNull(realmlist);
        return realmlist.Split(LineEnding, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
    }

    /// <summary>Checks the realmlist contract; returns normally when the text is valid.</summary>
    /// <exception cref="InvalidRealmlistException">The text is not valid (see <see cref="InvalidRealmlistKind"/>).</exception>
    public static void Validate(string? text)
    {
        if (string.IsNullOrEmpty(text))
            throw new InvalidRealmlistException(InvalidRealmlistKind.Empty, "the realmlist is empty");

        if (System.Text.Encoding.UTF8.GetByteCount(text) > RealmlistAddress.MaxRealmlistBytes)
            throw new InvalidRealmlistException(InvalidRealmlistKind.InvalidValue, $"the realmlist must be at most {RealmlistAddress.MaxRealmlistBytes} bytes");

        var bad = text.FirstOrDefault(c => c > '\u007F');
        if (bad != '\0')
            throw new InvalidRealmlistException(
                InvalidRealmlistKind.NotAscii,
                $"it contains the non-ASCII character U+{(int)bad:X4}; use ASCII only (punycode for international host names)",
                LineOf(text, text.IndexOf(bad)));

        if (!text.EndsWith(LineEnding, StringComparison.Ordinal))
            throw new InvalidRealmlistException(InvalidRealmlistKind.BadLineEnding, "every line, including the last, must end with CR LF");
        for (var i = 0; i < text.Length; i++)
        {
            var bareCr = text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n');
            var bareLf = text[i] == '\n' && (i == 0 || text[i - 1] != '\r');
            if (bareCr || bareLf)
                throw new InvalidRealmlistException(
                    InvalidRealmlistKind.BadLineEnding, "line endings must be CR LF (found a bare CR or LF)", LineOf(text, i));
        }

        var lines = text[..^LineEnding.Length].Split(LineEnding);
        var seen = new HashSet<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            var number = index + 1;
            var line = lines[index];

            if (index == 0 && !line.StartsWith(Prefix, StringComparison.Ordinal))
                throw new InvalidRealmlistException(
                    InvalidRealmlistKind.WrongFirstLine, $"the first line must be \"set realmlist <host>\" in lowercase", number);

            var parts = line.Split(' ');
            if (parts[0] != "set" || parts.Length < 3 || parts[1].Length == 0)
                throw new InvalidRealmlistException(
                    InvalidRealmlistKind.MalformedLine, "a line must have the form \"set <key> <value>\"", number);

            var key = parts[1];
            if (!AllowedKeys.Contains(key, StringComparer.Ordinal))
                throw new InvalidRealmlistException(
                    InvalidRealmlistKind.ForbiddenKey,
                    key == "portal"
                        ? "the key \"portal\" is only valid for WoD/Legion clients and is not supported yet"
                        : $"the key \"{key}\" is not allowed (only realmlist and patchlist)",
                    number);
            if (index == 0 && key != "realmlist")
                throw new InvalidRealmlistException(
                    InvalidRealmlistKind.WrongFirstLine, "the first line must be \"set realmlist <host>\" in lowercase", number);
            if (!seen.Add(key))
                throw new InvalidRealmlistException(InvalidRealmlistKind.DuplicateKey, $"the key \"{key}\" appears more than once", number);

            if (parts.Length != 3 || parts[2].Length == 0)
                throw new InvalidRealmlistException(
                    InvalidRealmlistKind.InvalidValue,
                    parts.Length > 3 ? "the value must not contain spaces" : "the value is missing",
                    number);
            if (parts[2].Any(c => c is '"' or '\'' || char.IsControl(c)))
                throw new InvalidRealmlistException(
                    InvalidRealmlistKind.InvalidValue, "the value must not contain quotes or control characters", number);

            if (key == "realmlist")
                RealmlistAddress.ValidateRealmlistHost(parts[2], number);
            else
                RealmlistAddress.ValidatePatchlistValue(parts[2], number);
        }
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, Math.Max(index, 0)).Count('\n') + 1;
}
