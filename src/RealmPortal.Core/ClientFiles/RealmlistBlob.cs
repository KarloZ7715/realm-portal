using System.Text.RegularExpressions;

namespace RealmPortal.Core.ClientFiles;

/// <summary>A validated realmlist text: ASCII lines <c>set &lt;key&gt; &lt;value&gt;</c> with whitelisted keys.</summary>
/// <param name="Lines">The lines exactly as given (without terminators); the first is <c>set realmlist &lt;host&gt;</c>.</param>
/// <param name="Values">Value per key (keys are lowercase: <c>realmlist</c>, <c>patchlist</c>, <c>portal</c>).</param>
internal sealed record RealmlistBlob(IReadOnlyList<string> Lines, IReadOnlyDictionary<string, string> Values)
{
    public const string RealmlistKey = "realmlist";
    public const string PatchlistKey = "patchlist";
    public const string PortalKey = "portal";

    private static readonly Regex Line = new(@"^set (?<key>[a-z]+) (?<value>[\x21\x23-\x7E]+)\z", RegexOptions.CultureInvariant);

    public string Host => Values[RealmlistKey];

    /// <summary>Parses and validates a realmlist text; line terminators may be CR LF or LF.</summary>
    /// <param name="allowPortal">Whether the <c>portal</c> key is accepted (WoD and Legion clients only).</param>
    /// <exception cref="ClientFilesException"><see cref="ClientFilesErrorCode.InvalidRealmlist"/> for any contract violation.</exception>
    public static RealmlistBlob Parse(string? text, bool allowPortal)
    {
        if (string.IsNullOrEmpty(text))
            throw Invalid("the realmlist is empty");

        var parts = text.Split('\n');
        // A final terminator produces one trailing empty element; anything else empty is a blank line.
        var count = parts[^1].Length == 0 ? parts.Length - 1 : parts.Length;
        var lines = new List<string>(count);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            var line = parts[i].EndsWith('\r') ? parts[i][..^1] : parts[i];
            var match = Line.Match(line);
            if (!match.Success)
                throw Invalid($"line {i + 1} is not 'set <key> <value>' in printable ASCII without quotes or spaces");

            var key = match.Groups["key"].Value;
            var allowed = key is RealmlistKey or PatchlistKey || (allowPortal && key == PortalKey);
            if (!allowed)
                throw Invalid($"line {i + 1} uses the key '{key}', which is not allowed");
            if (i == 0 && key != RealmlistKey)
                throw Invalid("the first line must be 'set realmlist <host>'");
            if (!values.TryAdd(key, match.Groups["value"].Value))
                throw Invalid($"the key '{key}' appears more than once");

            lines.Add(line);
        }

        if (count == 0 || !values.ContainsKey(RealmlistKey))
            throw Invalid("the first line must be 'set realmlist <host>'");

        return new RealmlistBlob(lines, values);
    }

    /// <summary>The realm name is written inside quotes, so quotes and control characters are rejected.</summary>
    public static void ValidateRealm(string? realm)
    {
        if (realm is not null && realm.Any(c => char.IsControl(c) || c == '"'))
            throw Invalid("the realm name contains quotes or control characters");
    }

    private static ClientFilesException Invalid(string reason) =>
        new(ClientFilesErrorCode.InvalidRealmlist, string.Empty, $"Invalid realmlist: {reason}.");
}
