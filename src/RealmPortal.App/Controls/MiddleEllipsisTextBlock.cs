using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace RealmPortal.App.Controls;

/// <summary>Fits a path to its actual layout width with a middle ellipsis, keeping both ends.
/// The tooltip and accessible name always retain the full, unmodified path.</summary>
public class MiddleEllipsisTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> FullTextProperty =
        AvaloniaProperty.Register<MiddleEllipsisTextBlock, string?>(nameof(FullText));

    static MiddleEllipsisTextBlock() => AffectsMeasure<MiddleEllipsisTextBlock>(FullTextProperty);

    public string? FullText { get => GetValue(FullTextProperty); set => SetValue(FullTextProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FullTextProperty)
        {
            ToolTip.SetTip(this, FullText);
            AutomationProperties.SetName(this, FullText);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var full = FullText ?? string.Empty;
        var width = Math.Max(0, availableSize.Width - Padding.Left - Padding.Right);
        Text = Fit(full, width);
        return base.MeasureOverride(availableSize);
    }

    private string Fit(string full, double width)
    {
        double Measure(string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground).WidthIncludingTrailingWhitespace;
        if (double.IsPositiveInfinity(width) || Measure(full) <= width) return full;
        if (Measure("…") > width) return string.Empty;

        // Never split a surrogate pair or a combining-character sequence in a user path.
        var boundaries = StringInfo.ParseCombiningCharacters(full);
        string Candidate(int count)
        {
            var prefixCount = (count + 1) / 2;
            var suffixCount = count / 2;
            var prefixEnd = prefixCount == boundaries.Length ? full.Length : boundaries[prefixCount];
            var suffixStart = suffixCount == 0 ? full.Length : boundaries[boundaries.Length - suffixCount];
            return full[..prefixEnd] + "…" + full[suffixStart..];
        }
        var low = 0;
        var high = Math.Max(0, boundaries.Length - 1);
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Measure(Candidate(middle)) <= width) low = middle;
            else high = middle - 1;
        }
        return Candidate(low);
    }
}
