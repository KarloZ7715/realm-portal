using RealmPortal.App.Markdown;

namespace RealmPortal.App.Tests;

/// <summary>The small Markdown subset used to show THIRD_PARTY_LICENSES.md on the About screen (#199).</summary>
public class MarkdownLiteTests
{
    [Fact]
    public void ParsesHeadingsParagraphsAndListItems()
    {
        var blocks = MarkdownLite.Parse("# Title\n\nFirst line\nsecond line.\n\n## Notes\n- one\n  continued\n- two\n#nospace");

        var title = Assert.IsType<MarkdownHeading>(blocks[0]);
        Assert.Equal(1, title.Level);
        Assert.Equal("Title", MarkdownLite.PlainText(title.Spans));
        Assert.Equal("First line second line.", MarkdownLite.PlainText(Assert.IsType<MarkdownParagraph>(blocks[1]).Spans));
        Assert.Equal(2, Assert.IsType<MarkdownHeading>(blocks[2]).Level);
        Assert.Equal("one continued", MarkdownLite.PlainText(Assert.IsType<MarkdownListItem>(blocks[3]).Spans));
        Assert.Equal("two", MarkdownLite.PlainText(Assert.IsType<MarkdownListItem>(blocks[4]).Spans));
        // "#" without a space is not a heading.
        Assert.Equal("#nospace", MarkdownLite.PlainText(Assert.IsType<MarkdownParagraph>(blocks[5]).Spans));
    }

    [Fact]
    public void ParsesInlineCodeAndBoldAndKeepsUnmatchedMarkersLiteral()
    {
        var spans = MarkdownLite.ParseInline("see `LICENSE` and **CC0 1.0**, a ` stray and ** stray");

        Assert.Equal(
            [new MarkdownSpan("see "), new MarkdownSpan("LICENSE", Code: true), new MarkdownSpan(" and "),
             new MarkdownSpan("CC0 1.0", Bold: true), new MarkdownSpan(", a ` stray and ** stray")],
            spans);
    }

    [Fact]
    public void ParsesTablesSkippingTheSeparatorRow()
    {
        var table = Assert.IsType<MarkdownTable>(Assert.Single(MarkdownLite.Parse(
            "| Package | Version | Files |\n|---|:---:|---|\n| Avalonia | 11.3.22 | `a|b/` |\n| PeNet | 5.1.0 | |")));

        Assert.Equal(["Package", "Version", "Files"], table.Header.Select(MarkdownLite.PlainText));
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["Avalonia", "11.3.22", "a|b/"], table.Rows[0].Select(MarkdownLite.PlainText));
        Assert.True(table.Rows[0][2].Single().Code);
        Assert.Equal(["PeNet", "5.1.0", ""], table.Rows[1].Select(MarkdownLite.PlainText));
    }

    [Fact]
    public void EmptyInputHasNoBlocks()
    {
        Assert.Empty(MarkdownLite.Parse(null));
        Assert.Empty(MarkdownLite.Parse("  \n\n"));
    }

    [Fact]
    public void TheShippedLicensesFileLeavesNoMarkdownSyntaxInTheText()
    {
        var blocks = MarkdownLite.Parse(File.ReadAllText(Repo.Path("THIRD_PARTY_LICENSES.md")));

        Assert.Contains(blocks, b => b is MarkdownHeading { Level: 1 });
        var tables = blocks.OfType<MarkdownTable>().ToList();
        Assert.NotEmpty(tables);
        Assert.Contains(tables.SelectMany(t => t.Rows), row => MarkdownLite.PlainText(row[0]) == "Avalonia");
        Assert.All(tables, t => Assert.All(t.Rows, row => Assert.DoesNotContain(row, cell => MarkdownLite.PlainText(cell).StartsWith("---", StringComparison.Ordinal))));

        var texts = blocks.SelectMany(Spans).Where(s => !s.Code).Select(s => s.Text).ToList();
        Assert.DoesNotContain(texts, t => t.Contains('`', StringComparison.Ordinal) || t.Contains("**", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.TrimStart().StartsWith('#') || t.TrimStart().StartsWith('|'));
    }

    private static IEnumerable<MarkdownSpan> Spans(MarkdownBlock block) => block switch
    {
        MarkdownHeading h => h.Spans,
        MarkdownParagraph p => p.Spans,
        MarkdownListItem l => l.Spans,
        MarkdownTable t => t.Header.SelectMany(c => c).Concat(t.Rows.SelectMany(r => r.SelectMany(c => c))),
        _ => [],
    };
}
