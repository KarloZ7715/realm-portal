using System.Text;

namespace RealmPortal.Core.Launch;

/// <summary>Splits a command template into words, shell-like but without any expansion.</summary>
public static class LaunchCommandTemplate
{
    /// <summary>
    /// Whitespace separates words; single quotes keep everything literal; double quotes keep whitespace and honor <c>\"</c> and <c>\\</c>.
    /// A backslash outside quotes is a literal character (Windows paths).
    /// </summary>
    public static List<string> Split(string template)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var inWord = false;
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c is '\'' or '"')
            {
                inWord = true;
                var close = c;
                i++;
                for (; ; i++)
                {
                    if (i >= template.Length)
                        throw new ArgumentException($"The command template has an unclosed {close} quote.", nameof(template));
                    var q = template[i];
                    if (q == close)
                        break;
                    if (close == '"' && q == '\\' && i + 1 < template.Length && template[i + 1] is '"' or '\\')
                        q = template[++i];
                    current.Append(q);
                }
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    inWord = false;
                }
            }
            else
            {
                inWord = true;
                current.Append(c);
            }
        }

        if (inWord)
            words.Add(current.ToString());
        return words;
    }
}
