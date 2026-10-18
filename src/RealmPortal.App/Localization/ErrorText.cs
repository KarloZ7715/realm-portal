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

    /// <summary>
    /// Body for a client-files code without a <see cref="Consistency"/>.
    /// The clients screen reports service results through <see cref="Body(ServiceError)"/>.
    /// This overload is a pre-check helper: <see cref="PreCheckWritesNothing"/> codes are refusals before any write,
    /// so they keep the unchanged sentence. Every other code uses the unknown-state sentence.
    /// </summary>
    public static string Body(ClientFilesErrorCode code, string? path = null) =>
        Compose(Body(code.ToString(), path), PreCheckWritesNothing(code) ? Loc.Get("error.state.unchanged") : Loc.Get("error.state.unknown"));

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
            // The switch is already applied, so this text must not say that nothing changed.
            return Loc.Get("error.launchfailed.applied.body");
        }

        return Compose(CodeBody(error), StateSentence(error.Consistency));
    }

    /// <summary>The code's own sentence, with placeholders filled and no claim about what was left on disk.</summary>
    private static string CodeBody(ServiceError error) => error.Code switch
    {
        ServiceErrorCode.PermissionDenied or ServiceErrorCode.ReadOnlyFs or ServiceErrorCode.PathNotFound
            or ServiceErrorCode.PathEscapesClient or ServiceErrorCode.DiskFull or ServiceErrorCode.FileSharingViolation
            or ServiceErrorCode.ClientExecutableNotFound or ServiceErrorCode.ClientUnreadable
            or ServiceErrorCode.ClientNotAPortableExecutable or ServiceErrorCode.ClientNoVersionResource =>
            Body(error.Code.ToString(), error.Path ?? string.Empty),
        ServiceErrorCode.ProcessCheckFailed =>
            Body(error.Code.ToString()),
        ServiceErrorCode.ServerNameTaken =>
            Body(error.Code.ToString(), error.ServerName ?? string.Empty),
        ServiceErrorCode.IncompatibleClient =>
            Body(error.Code.ToString(), error.RequiredBuild, error.ClientBuild),
        ServiceErrorCode.UndoConflict =>
            Body(error.Code.ToString(), FormatFileList(error.ConflictingFiles)),
        ServiceErrorCode.InvalidServerInput =>
            Body(error.Code.ToString(), error.Field ?? string.Empty),
        // error.unexpected.body is a "{0}" slot for log text. Use the generic body, which has no placeholder.
        ServiceErrorCode.Unexpected =>
            Body("unknown"),
        _ => Body(error.Code.ToString()),
    };

    /// <summary>
    /// What to append about the files left behind. <see cref="Consistency"/> has no null value;
    /// anything other than unchanged, rolled back, or inconsistent uses the unknown-state sentence.
    /// </summary>
    private static string? StateSentence(Consistency consistency) => consistency switch
    {
        Consistency.Unchanged => Loc.Get("error.state.unchanged"),
        Consistency.RolledBack => Loc.Get("error.state.rolledback"),
        Consistency.Inconsistent => Loc.Get("error.inconsistentstate.body"),
        _ => Loc.Get("error.state.unknown"),
    };

    private static string Compose(string body, string? state)
    {
        if (string.IsNullOrEmpty(state) || body.Contains(state, StringComparison.Ordinal))
        {
            return body;
        }

        body = body.TrimEnd();
        return body.Length == 0 ? state : $"{body} {state}";
    }

    /// <summary>Codes that Core raises only to refuse a change before it writes client files.</summary>
    private static bool PreCheckWritesNothing(ClientFilesErrorCode code) => code is
        ClientFilesErrorCode.GameRunning
        or ClientFilesErrorCode.UnsupportedClient
        or ClientFilesErrorCode.InvalidRealmlist
        or ClientFilesErrorCode.NothingToUndo;

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
