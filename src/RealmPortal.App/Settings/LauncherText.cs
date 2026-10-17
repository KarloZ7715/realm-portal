using System.Text;

namespace RealmPortal.App.Settings;

/// <summary>
/// Converts between the plain text boxes of the Settings screen and the lists the launch settings hold.
/// Only syntax lives here (quotes, one variable per line); whether a value is usable is the service's decision.
/// </summary>
public static class LauncherText
{
    /// <summary>Splits on spaces; single or double quotes group words ("a b" is one token); a backslash escapes the next character.</summary>
    public static IReadOnlyList<string> Tokens(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        var current = new StringBuilder();
        var inToken = false;
        char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && quote != '\'')
            {
                current.Append(text[++i]);
                inToken = true;
            }
            else if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inToken = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
            }
            else
            {
                current.Append(c);
                inToken = true;
            }
        }

        if (inToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>The inverse of <see cref="Tokens"/>: tokens with spaces or quotes are quoted.</summary>
    public static string Join(IEnumerable<string>? tokens) =>
        string.Join(' ', (tokens ?? []).Select(t => t.Length == 0 || t.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '\\')
            ? "\"" + t.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : t));

    /// <summary>
    /// Parses <c>KEY=value</c> lines (blank lines are skipped). Returns the 1-based number of the first line that is not
    /// <c>KEY=value</c> (no '=' or an empty key), or null when all lines are fine.
    /// </summary>
    public static int? TryParseEnvironment(string? text, out IReadOnlyDictionary<string, string?> result)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        result = map;
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                return i + 1;
            }

            map[line[..eq].Trim()] = line[(eq + 1)..];
        }

        return null;
    }

    public static string JoinEnvironment(IReadOnlyDictionary<string, string?>? environment) =>
        string.Join('\n', (environment ?? new Dictionary<string, string?>()).Where(p => p.Value is not null).Select(p => $"{p.Key}={p.Value}"));
}
