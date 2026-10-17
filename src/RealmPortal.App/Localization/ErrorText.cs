using RealmPortal.Core.ClientFiles;

namespace RealmPortal.App.Localization;

/// <summary>
/// Turns the stable error codes of the core into ES/EN text. The core never supplies UI text, only a code and the path involved.
/// Codes are matched by name so a code added to the core only needs two new entries in the string tables
/// (<c>error.&lt;code&gt;.title</c> and <c>error.&lt;code&gt;.body</c>); an unknown code falls back to a generic message.
/// </summary>
public static class ErrorText
{
    public static string Title(ClientFilesErrorCode code) => Title(code.ToString());

    public static string Body(ClientFilesErrorCode code, string? path = null) => Body(code.ToString(), path);

    public static string Title(string code) => Lookup(code, "title");

    /// <summary>The body; <c>{0}</c> in the text is replaced by <paramref name="path"/>.</summary>
    public static string Body(string code, string? path = null) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Lookup(code, "body"), path ?? string.Empty);

    private static string Lookup(string code, string part)
    {
        var key = $"error.{code.ToLowerInvariant()}.{part}";
        var text = Loc.Instance[key];
        return text == $"!{key}!" ? Loc.Instance[$"error.unknown.{part}"] : text;
    }
}
