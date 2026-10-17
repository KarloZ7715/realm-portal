using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace RealmPortal.App.Localization;

/// <summary>
/// <c>{loc:T key}</c>: the localized text for <c>key</c>, kept up to date when the language changes.
/// Use it anywhere a string property is set in XAML.
/// </summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
}
