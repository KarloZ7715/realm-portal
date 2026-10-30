using System.Text;

namespace RealmPortal.App.Markdown;

/// <summary>A piece of inline text: plain, <c>**bold**</c> or <c>`code`</c>.</summary>
public sealed record MarkdownSpan(string Text, bool Bold = false, bool Code = false);

/// <summary>A block of the document.</summary>
public abstract record MarkdownBlock;

/// <summary><c>#</c> to <c>######</c> heading.</summary>
public sealed record MarkdownHeading(int Level, IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

/// <summary>Consecutive non-blank lines joined with spaces.</summary>
public sealed record MarkdownParagraph(IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

/// <summary>A <c>- </c> or <c>* </c> list item, with its indented continuation lines.</summary>
public sealed record MarkdownListItem(IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

/// <summary>A pipe table: header cells and the rows below the <c>|---|</c> separator.</summary>
public sealed record MarkdownTable(
    IReadOnlyList<IReadOnlyList<MarkdownSpan>> Header,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownSpan>>> Rows) : MarkdownBlock;

/// <summary>
/// Parses the small Markdown subset used by <c>THIRD_PARTY_LICENSES.md</c>: ATX headings, paragraphs, <c>-</c>/<c>*</c>
/// list items, pipe tables, <c>**bold**</c> and <c>`code`</c>. Anything else (links, HTML, emphasis with one star) stays
/// literal text: the result is only ever shown as text, never interpreted.
/// </summary>
public static class MarkdownLite
{
    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return blocks;
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                i++;
                continue;
            }

            if (HeadingLevel(trimmed) is { } level)
            {
                blocks.Add(new MarkdownHeading(level, ParseInline(trimmed[(level + 1)..].Trim())));
                i++;
                continue;
            }

            if (trimmed.StartsWith('|'))
            {
                var tableLines = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('|'))
                {
                    tableLines.Add(lines[i].Trim());
                    i++;
                }

                blocks.Add(ParseTable(tableLines));
                continue;
            }

            if (IsListItem(trimmed))
            {
                var text = new StringBuilder(trimmed[2..].Trim());
                i++;
                while (i < lines.Length && lines[i].Length > 0 && char.IsWhiteSpace(lines[i][0]) && lines[i].Trim().Length > 0
                       && !IsListItem(lines[i].Trim()))
                {
                    text.Append(' ').Append(lines[i].Trim());
                    i++;
                }

                blocks.Add(new MarkdownListItem(ParseInline(text.ToString())));
                continue;
            }

            var paragraph = new StringBuilder(trimmed);
            i++;
            while (i < lines.Length && lines[i].Trim() is { Length: > 0 } next
                   && HeadingLevel(next) is null && !next.StartsWith('|') && !IsListItem(next))
            {
                paragraph.Append(' ').Append(next);
                i++;
            }

            blocks.Add(new MarkdownParagraph(ParseInline(paragraph.ToString())));
        }

        return blocks;
    }

    /// <summary>Splits <paramref name="text"/> into plain, bold and code spans. An unmatched marker stays literal.</summary>
    public static IReadOnlyList<MarkdownSpan> ParseInline(string text)
    {
        var spans = new List<MarkdownSpan>();
        var plain = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '`' && text.IndexOf('`', i + 1) is var end and > 0)
            {
                Flush();
                spans.Add(new MarkdownSpan(text[(i + 1)..end], Code: true));
                i = end + 1;
                continue;
            }

            if (string.CompareOrdinal(text, i, "**", 0, 2) == 0 && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var close and > 0)
            {
                Flush();
                spans.Add(new MarkdownSpan(text[(i + 2)..close], Bold: true));
                i = close + 2;
                continue;
            }

            plain.Append(text[i]);
            i++;
        }

        Flush();
        return spans;

        void Flush()
        {
            if (plain.Length > 0)
            {
                spans.Add(new MarkdownSpan(plain.ToString()));
                plain.Clear();
            }
        }
    }

    /// <summary>Plain text of spans, without markers (used for headings in tests and accessible names).</summary>
    public static string PlainText(IEnumerable<MarkdownSpan> spans) => string.Concat(spans.Select(s => s.Text));

    private static int? HeadingLevel(string trimmed)
    {
        var level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
        {
            level++;
        }

        return level is >= 1 and <= 6 && trimmed.Length > level && trimmed[level] == ' ' ? level : null;
    }

    private static bool IsListItem(string trimmed) =>
        trimmed.Length > 2 && (trimmed[0] == '-' || trimmed[0] == '*') && trimmed[1] == ' ';

    private static MarkdownTable ParseTable(List<string> lines)
    {
        var cells = lines.Select(SplitRow).ToList();
        var header = cells[0];
        var bodyStart = cells.Count > 1 && cells[1].All(IsSeparatorCell) ? 2 : 1;
        var rows = cells.Skip(bodyStart)
            .Select(row => (IReadOnlyList<IReadOnlyList<MarkdownSpan>>)row.Select(ParseInline).ToList())
            .ToList();
        return new MarkdownTable(header.Select(ParseInline).ToList(), rows);
    }

    private static bool IsSeparatorCell(string cell) =>
        cell.Length > 0 && cell.All(c => c is '-' or ':' or ' ');

    /// <summary>Cells of a <c>| a | b |</c> row; a pipe inside <c>`code`</c> does not split.</summary>
    private static List<string> SplitRow(string line)
    {
        var body = line.Trim();
        if (body.StartsWith('|'))
        {
            body = body[1..];
        }

        if (body.EndsWith('|'))
        {
            body = body[..^1];
        }

        var cells = new List<string>();
        var current = new StringBuilder();
        var inCode = false;
        foreach (var c in body)
        {
            if (c == '`')
            {
                inCode = !inCode;
            }

            if (c == '|' && !inCode)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }
}
