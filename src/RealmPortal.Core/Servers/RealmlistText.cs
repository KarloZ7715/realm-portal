namespace RealmPortal.Core.Servers;

/// <summary>Builds the canonical realmlist text.</summary>
public static class RealmlistText
{
    /// <summary>The only line terminator used in realmlist text.</summary>
    public const string LineEnding = "\r\n";

    /// <summary>
    /// Returns <c>set realmlist {host}</c> followed by the extra lines, each line terminated by <c>\r\n</c>
    /// (the last one too). Extra lines are trimmed; blank ones are dropped.
    /// </summary>
    /// <exception cref="ArgumentException">The host or an extra line contains an embedded line break.</exception>
    public static string Build(string host, IEnumerable<string>? extraLines = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Contains('\r') || host.Contains('\n'))
            throw new ArgumentException("The host must not contain line breaks.", nameof(host));

        var text = new System.Text.StringBuilder();
        text.Append("set realmlist ").Append(host).Append(LineEnding);

        foreach (var raw in extraLines ?? [])
        {
            var line = raw?.Trim() ?? "";
            if (line.Length == 0)
                continue;
            if (line.Contains('\r') || line.Contains('\n'))
                throw new ArgumentException("A realmlist line must not contain line breaks.", nameof(extraLines));
            text.Append(line).Append(LineEnding);
        }

        return text.ToString();
    }
}
