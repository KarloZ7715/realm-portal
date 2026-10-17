using System.Text;
using System.Text.RegularExpressions;

namespace RealmPortal.Core.ClientFiles;

/// <summary>Pure functions that compute the new content of <c>realmlist.wtf</c> and <c>Config.wtf</c>.</summary>
internal static class ClientFileContent
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    // Config.wtf is handled as Latin-1 so that every byte of lines we do not manage survives unchanged.
    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>
    /// realmlist.wtf: the blob exactly as given, one line per key, each line terminated. Keeps the BOM and the line
    /// ending style of an existing file; a new file (or one without any line break) uses CR LF.
    /// </summary>
    /// <remarks>
    /// This is the only place that decides which realmlist lines reach the file: today every line of the validated
    /// blob (<c>set realmlist</c> and, if present, <c>set patchlist</c>) is written as is.
    /// </remarks>
    public static byte[] BuildRealmlist(byte[]? existing, RealmlistBlob blob)
    {
        var (hasBom, body) = Split(existing);
        var eol = DetectEol(body, "\r\n");
        var text = string.Concat(blob.Lines.Select(line => line + eol));
        return Join(hasBom, Encoding.ASCII.GetBytes(text));
    }

    /// <summary>
    /// Sets or removes managed <c>SET &lt;key&gt; "value"</c> lines in Config.wtf. For each managed key the first
    /// occurrence (key matched case-insensitively) gets only its value replaced, keeping its capitalization and
    /// spacing, and later duplicates are deleted; a <c>null</c> value deletes the line; a missing key with a value is
    /// appended in the standard form. Every other line, the BOM and the line endings are kept byte for byte.
    /// </summary>
    /// <param name="existing">Current content, or <c>null</c> when the file does not exist.</param>
    /// <param name="managed">Canonical key (for example <c>realmList</c>) to new value, or <c>null</c> to remove; in append order.</param>
    public static byte[] BuildConfig(byte[]? existing, IReadOnlyList<KeyValuePair<string, string?>> managed)
    {
        var (hasBom, body) = Split(existing);
        var text = Latin1.GetString(body);
        var eol = DetectEol(body, "\r\n");

        var alternation = string.Join('|', managed.Select(m => Regex.Escape(m.Key)));
        var pattern = new Regex(
            $@"^(?<pre>[ \t]*SET[ \t]+)(?<key>{alternation})(?:(?<sp>[ \t]+)(?<val>.*))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var wanted = managed.ToDictionary(m => m.Key, m => m.Value, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = new StringBuilder(text.Length + 64);
        var position = 0;
        while (position < text.Length)
        {
            var end = text.IndexOf('\n', position);
            var next = end < 0 ? text.Length : end + 1;
            var contentEnd = end < 0 ? text.Length : end;
            if (end > position && text[end - 1] == '\r')
                contentEnd = end - 1;

            var line = text[position..contentEnd];
            var terminator = text[contentEnd..next];
            var match = pattern.Match(line);
            if (!match.Success)
            {
                result.Append(line).Append(terminator);
            }
            else
            {
                var key = match.Groups["key"].Value;
                var value = wanted[key];
                if (seen.Add(key) && value is not null)
                {
                    var spacing = match.Groups["sp"].Success ? match.Groups["sp"].Value : " ";
                    result.Append(match.Groups["pre"].Value).Append(key).Append(spacing).Append(Quote(value)).Append(terminator);
                }
                // else: a duplicate or a line to remove; dropped together with its terminator.
            }

            position = next;
        }

        foreach (var (key, value) in managed)
        {
            if (value is null || seen.Contains(key))
                continue;

            if (result.Length > 0 && result[^1] != '\n')
                result.Append(eol);
            result.Append("SET ").Append(key).Append(' ').Append(Quote(value)).Append(eol);
        }

        return Join(hasBom, Latin1.GetBytes(result.ToString()));
    }

    private static string Quote(string value) => "\"" + Latin1.GetString(Encoding.UTF8.GetBytes(value)) + "\"";

    private static (bool HasBom, byte[] Body) Split(byte[]? existing)
    {
        if (existing is null)
            return (false, []);
        return existing.AsSpan().StartsWith(Bom)
            ? (true, existing.AsSpan(Bom.Length).ToArray())
            : (false, existing);
    }

    /// <summary>CR LF when the content has any CR LF, LF when it only has bare LFs, otherwise the default.</summary>
    private static string DetectEol(byte[] body, string fallback)
    {
        if (body.AsSpan().IndexOf("\r\n"u8) >= 0)
            return "\r\n";
        return body.AsSpan().IndexOf((byte)'\n') >= 0 ? "\n" : fallback;
    }

    private static byte[] Join(bool bom, byte[] body) => bom ? [.. Bom, .. body] : body;
}
