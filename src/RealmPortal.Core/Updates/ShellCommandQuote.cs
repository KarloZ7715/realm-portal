using System.Text;

namespace RealmPortal.Core.Updates;

/// <summary>POSIX single-quote escaping for copyable install commands (docs/updates-and-status.md).</summary>
public static class ShellCommandQuote
{
    /// <summary>
    /// Returns <c>null</c> when <paramref name="absolutePath"/> contains a control character
    /// (including newline); otherwise quotes it so it is safe to paste into a POSIX shell.
    /// </summary>
    public static string? QuoteAbsolutePath(string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);
        foreach (var ch in absolutePath)
        {
            if (char.IsControl(ch))
                return null;
        }

        var sb = new StringBuilder(absolutePath.Length + 2);
        sb.Append('\'');
        foreach (var ch in absolutePath)
        {
            if (ch == '\'')
                sb.Append("'\\''");
            else
                sb.Append(ch);
        }

        sb.Append('\'');
        return sb.ToString();
    }

    public static string? ChmodUxCommand(string absolutePath)
    {
        var q = QuoteAbsolutePath(absolutePath);
        return q is null ? null : "chmod u+x " + q;
    }

    public static string? AptInstallCommand(string absolutePath)
    {
        var q = QuoteAbsolutePath(absolutePath);
        return q is null ? null : "sudo apt install " + q;
    }
}
