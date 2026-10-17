namespace RealmPortal.App.Localization;

/// <summary>
/// Maps stable error code names to ES/EN string-table keys. Unknown codes fall back to <c>error.unknown</c>.
/// When PR #34 merges, this type is shared with client-files errors; until then it covers service codes too.
/// </summary>
public static class ErrorText
{
    public static string Title(string code) => Lookup(code, "title");

    public static string Body(string code, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Lookup(code, "body"), args);

    private static string Lookup(string code, string part)
    {
        var key = $"error.{code.ToLowerInvariant()}.{part}";
        var text = Loc.Instance[key];
        return text == $"!{key}!" ? Loc.Instance[$"error.unknown.{part}"] : text;
    }
}
