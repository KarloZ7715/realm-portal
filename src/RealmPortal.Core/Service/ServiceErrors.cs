using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Service;

internal static class ServiceErrors
{
    public static ServiceError FromException(Exception ex, Consistency consistency = Consistency.Unchanged) =>
        ex switch
        {
            ClientFilesException cfe => FromClientFiles(cfe, consistency),
            InvalidRealmlistException ire => new(
                ServiceErrorCode.InvalidRealmlist,
                ServerName: ire.ServerName,
                LineNumber: ire.LineNumber,
                RealmlistKind: ire.Kind,
                Consistency: consistency,
                Diagnostic: ex.Message),
            ClientVersionException cve => FromClientVersion(cve, consistency),
            StoreException se => FromStore(se, consistency),
            ArgumentException ae => FromArgument(ae, consistency),
            _ => new(ServiceErrorCode.Unexpected, Consistency: consistency, Diagnostic: ex.ToString()),
        };

    public static ServiceError FromClientFiles(ClientFilesException ex, Consistency consistency = Consistency.Unchanged)
    {
        if (!Enum.TryParse<ServiceErrorCode>(ex.Code.ToString(), out var code))
            return new(ServiceErrorCode.Unexpected, Path: ex.Path, Consistency: consistency, Diagnostic: ex.Message);

        return code switch
        {
            ServiceErrorCode.UnsupportedClient => new(code, Path: ex.Path, ClientVersion: ex.Message, Consistency: consistency, Diagnostic: ex.Message),
            ServiceErrorCode.UndoConflict => new(code, Path: ex.Path, Consistency: Consistency.Pending, Diagnostic: ex.Message),
            _ => new(code, Path: ex.Path, Consistency: consistency, Diagnostic: ex.Message),
        };
    }

    public static ServiceError FromClientVersion(ClientVersionException ex, Consistency consistency = Consistency.Unchanged)
    {
        var code = ex.Kind switch
        {
            ClientVersionErrorKind.ExecutableNotFound => Directory.Exists(ex.Path)
                ? ServiceErrorCode.ClientExecutableNotFound
                : ServiceErrorCode.PathNotFound,
            ClientVersionErrorKind.NotAPortableExecutable => ServiceErrorCode.ClientNotAPortableExecutable,
            ClientVersionErrorKind.NoVersionResource => ServiceErrorCode.ClientNoVersionResource,
            ClientVersionErrorKind.Unreadable => ServiceErrorCode.ClientUnreadable,
            _ => ServiceErrorCode.Unexpected,
        };
        return new(code, Path: ex.Path, Consistency: consistency, Diagnostic: ex.Message);
    }

    public static ServiceError FromStore(StoreException ex, Consistency consistency = Consistency.Unchanged)
    {
        var msg = ex.Message;
        if (msg.Contains("Nothing to undo", StringComparison.OrdinalIgnoreCase))
            return new(ServiceErrorCode.NothingToUndo, Consistency: consistency, Diagnostic: msg);
        if (msg.Contains("previous server no longer exists", StringComparison.OrdinalIgnoreCase))
            return new(ServiceErrorCode.UndoTargetMissing, Consistency: consistency, Diagnostic: msg);
        if (msg.Contains("already exists", StringComparison.OrdinalIgnoreCase) && msg.Contains("named", StringComparison.OrdinalIgnoreCase))
        {
            var start = msg.IndexOf('\'') + 1;
            var end = msg.IndexOf('\'', start);
            var name = start > 0 && end > start ? msg[start..end] : null;
            return new(ServiceErrorCode.ServerNameTaken, ServerName: name, Consistency: consistency, Diagnostic: msg);
        }

        if (msg.Contains("no server matches", StringComparison.OrdinalIgnoreCase))
            return new(ServiceErrorCode.ServerNotFound, Consistency: consistency, Diagnostic: msg);

        return new(ServiceErrorCode.StoreCorrupt, Consistency: consistency, Diagnostic: msg);
    }

    private static ServiceError FromArgument(ArgumentException ex, Consistency consistency)
    {
        var field = ex.ParamName switch
        {
            nameof(NewServer.Name) or "name" => "name",
            nameof(NewServer.Host) or "host" => "host",
            nameof(NewServer.Realm) or "realm" => "realm",
            nameof(NewServer.Account) or "account" => "account",
            nameof(NewServer.RequiredClientBuild) or "requiredClientBuild" => "build",
            _ => null,
        };
        if (field is not null)
            return new(ServiceErrorCode.InvalidServerInput, Field: field, Consistency: consistency, Diagnostic: ex.Message);

        return new(ServiceErrorCode.Unexpected, Consistency: consistency, Diagnostic: ex.Message);
    }

    public static ServiceError UndoConflict(IReadOnlyList<string> conflictingFiles) =>
        new(ServiceErrorCode.UndoConflict, ConflictingFiles: conflictingFiles, Consistency: Consistency.Pending);

    public static ServiceError Incompatible(int required, int client) =>
        new(ServiceErrorCode.IncompatibleClient, RequiredBuild: required, ClientBuild: client);

    public static ServiceError Inconsistent(ServiceError inner) =>
        inner with { Code = ServiceErrorCode.InconsistentState, Consistency = Consistency.Inconsistent };
}
