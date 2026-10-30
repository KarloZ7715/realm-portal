using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using RealmPortal.App.Markdown;

namespace RealmPortal.App.Controls;

/// <summary>
/// Shows a <see cref="MarkdownLite"/> document as native text blocks: headings, paragraphs, bullet items, and each table
/// row as a small entry (first cell in bold, the other cells as "Header: value" lines, which stay readable in a narrow
/// box). Text colour comes from the inherited <see cref="TextElement.ForegroundProperty"/>.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    static MarkdownView()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.Rebuild());
    }

    public MarkdownView()
    {
        Spacing = 6;
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        MarkdownBlock? previous = null;
        foreach (var block in MarkdownLite.Parse(Markdown))
        {
            var control = block switch
            {
                MarkdownHeading heading => Heading(heading, previous is null),
                MarkdownParagraph paragraph => Text(paragraph.Spans, "footnote"),
                MarkdownListItem item => ListItem(item),
                MarkdownTable table => Table(table),
                _ => null,
            };
            if (control is not null)
            {
                Children.Add(control);
            }

            previous = block;
        }
    }

    private Control Heading(MarkdownHeading heading, bool first)
    {
        var text = Text(heading.Spans, "body-bold");
        text.FontSize = heading.Level <= 1 ? 17 : 15;
        text.Margin = new Thickness(0, first ? 0 : 10, 0, 0);
        return text;
    }

    private Control ListItem(MarkdownListItem item)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("14,*") };
        var bullet = Text([new MarkdownSpan("•")], "footnote");
        var text = Text(item.Spans, "footnote");
        Grid.SetColumn(text, 1);
        grid.Children.Add(bullet);
        grid.Children.Add(text);
        return grid;
    }

    private Control Table(MarkdownTable table)
    {
        var rows = new StackPanel { Spacing = 4 };
        foreach (var row in table.Rows)
        {
            var entry = new StackPanel { Spacing = 1 };
            if (row.Count > 0)
            {
                entry.Children.Add(Text(row[0], "body-bold"));
            }

            var details = new List<MarkdownSpan>();
            for (var c = 1; c < row.Count; c++)
            {
                if (row[c].Count == 0)
                {
                    continue;
                }

                if (details.Count > 0)
                {
                    details.Add(new MarkdownSpan("\n"));
                }

                if (c < table.Header.Count)
                {
                    details.Add(new MarkdownSpan(MarkdownLite.PlainText(table.Header[c]) + ": ", Bold: true));
                }

                details.AddRange(row[c]);
            }

            if (details.Count > 0)
            {
                entry.Children.Add(Text(details, "footnote"));
            }

            rows.Children.Add(new Border
            {
                Child = entry,
                Padding = new Thickness(8, 5),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(0x10, 0, 0, 0)),
            });
        }

        return rows;
    }

    private TextBlock Text(IReadOnlyList<MarkdownSpan> spans, string style)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch };
        block.Classes.Add(style);
        block[!TextBlock.ForegroundProperty] = this[!TextElement.ForegroundProperty];
        var inlines = new InlineCollection();
        foreach (var span in spans)
        {
            if (span.Text == "\n")
            {
                inlines.Add(new LineBreak());
                continue;
            }

            var run = new Run(span.Text);
            if (span.Bold)
            {
                run.FontWeight = FontWeight.Bold;
            }

            if (span.Code && Resource("FontMono") is FontFamily mono)
            {
                run.FontFamily = mono;
                run.FontSize = 12;
            }

            inlines.Add(run);
        }

        block.Inlines = inlines;
        return block;
    }

    private object? Resource(string key) =>
        this.TryFindResource(key, out var value) || (Application.Current?.TryFindResource(key, out value) ?? false) ? value : null;
}
