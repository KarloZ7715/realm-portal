using RealmPortal.Core.Service;

namespace RealmPortal.App.Localization;

public static class ServiceErrorText
{
    public static string Title(ServiceError error) => ErrorText.Title(error.Code.ToString());

    public static string Body(ServiceError error) =>
        error.Code switch
        {
            ServiceErrorCode.PermissionDenied or ServiceErrorCode.ReadOnlyFs or ServiceErrorCode.PathNotFound
                or ServiceErrorCode.ClientExecutableNotFound or ServiceErrorCode.ClientUnreadable
                or ServiceErrorCode.ProcessCheckFailed =>
                ErrorText.Body(error.Code.ToString(), error.Path ?? string.Empty),
            ServiceErrorCode.ServerNameTaken =>
                ErrorText.Body(error.Code.ToString(), error.ServerName ?? string.Empty),
            ServiceErrorCode.IncompatibleClient =>
                ErrorText.Body(error.Code.ToString(), error.RequiredBuild, error.ClientBuild),
            ServiceErrorCode.UndoConflict =>
                ErrorText.Body(error.Code.ToString(), FormatFileList(error.ConflictingFiles)),
            ServiceErrorCode.InvalidServerInput =>
                ErrorText.Body(error.Code.ToString(), error.Field ?? string.Empty),
            ServiceErrorCode.Unexpected or ServiceErrorCode.InconsistentState or ServiceErrorCode.LaunchFailed
                when !string.IsNullOrEmpty(error.Diagnostic) =>
                ErrorText.Body("unexpected", error.Diagnostic),
            _ => ErrorText.Body(error.Code.ToString()),
        };

    private static string FormatFileList(IReadOnlyList<string>? files) =>
        files is null or { Count: 0 } ? string.Empty : string.Join(", ", files);
}
