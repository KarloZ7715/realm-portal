using RealmPortal.App.Service;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Service;

namespace RealmPortal.App.Localization;

/// <summary>
/// Turns the stable error codes of the core into ES/EN text. The core never supplies UI text, only a code and the parameters involved.
/// Codes are matched by name so a code added to the core only needs two new entries in the string tables
/// (<c>error.&lt;code&gt;.title</c> and <c>error.&lt;code&gt;.body</c>); an unknown code falls back to a generic message.
/// </summary>
public static class ErrorText
{
    public static string Title(ClientFilesErrorCode code) => Title(code.ToString());

    public static string Body(ClientFilesErrorCode code, string? path = null) => Body(code.ToString(), path);

    public static string Title(ServiceError error) =>
        PlayServiceResults.SwitchWasApplied(error)
            ? Loc.Get("error.launchfailed.applied.title")
            : Title(error.Code.ToString());

    /// <summary>The body for a service failure, filling the placeholders of that code from <paramref name="error"/>.</summary>
    public static string Body(ServiceError error)
    {
        if (PlayServiceResults.SwitchWasApplied(error))
        {
            // Diagnostic is English text for logs only, so it is not shown here.
            return Loc.Get("error.launchfailed.applied.body");
        }

        return error.Code switch
        {
            ServiceErrorCode.PermissionDenied or ServiceErrorCode.ReadOnlyFs or ServiceErrorCode.PathNotFound
                or ServiceErrorCode.PathEscapesClient or ServiceErrorCode.DiskFull or ServiceErrorCode.FileSharingViolation
                or ServiceErrorCode.ClientExecutableNotFound or ServiceErrorCode.ClientUnreadable
                or ServiceErrorCode.ClientNotAPortableExecutable or ServiceErrorCode.ClientNoVersionResource
                or ServiceErrorCode.ProcessCheckFailed =>
                Body(error.Code.ToString(), error.Path ?? string.Empty),
            ServiceErrorCode.ServerNameTaken =>
                Body(error.Code.ToString(), error.ServerName ?? string.Empty),
            ServiceErrorCode.IncompatibleClient =>
                Body(error.Code.ToString(), error.RequiredBuild, error.ClientBuild),
            ServiceErrorCode.UndoConflict =>
                Body(error.Code.ToString(), FormatFileList(error.ConflictingFiles)),
            ServiceErrorCode.InvalidServerInput =>
                Body(error.Code.ToString(), error.Field ?? string.Empty),
            ServiceErrorCode.Unexpected or ServiceErrorCode.InconsistentState or ServiceErrorCode.LaunchFailed
                when !string.IsNullOrEmpty(error.Diagnostic) =>
                Body("unexpected", error.Diagnostic),
            _ => Body(error.Code.ToString()),
        };
    }

    public static string Title(string code) => Lookup(code, "title");

    /// <summary>The body. Extra arguments fill <c>{0}</c>, <c>{1}</c>, …; with none, <c>{0}</c> is an empty path.</summary>
    public static string Body(string code, params object?[] args)
    {
        if (args.Length == 0)
        {
            args = [string.Empty];
        }

        return string.Format(System.Globalization.CultureInfo.CurrentCulture, Lookup(code, "body"), args);
    }

    private static string FormatFileList(IReadOnlyList<string>? files) =>
        files is null or { Count: 0 } ? string.Empty : string.Join(", ", files);

    private static string Lookup(string code, string part)
    {
        var key = $"error.{code.ToLowerInvariant()}.{part}";
        var text = Loc.Instance[key];
        return text == $"!{key}!" ? Loc.Instance[$"error.unknown.{part}"] : text;
    }
}
