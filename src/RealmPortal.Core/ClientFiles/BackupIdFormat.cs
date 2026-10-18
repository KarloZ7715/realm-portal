using System.Text.RegularExpressions;

namespace RealmPortal.Core.ClientFiles;

/// <summary>Validates backup ids produced by <see cref="ClientFilesApplier"/>.</summary>
public static partial class BackupIdFormat
{
    private static readonly Regex Pattern = BackupIdRegex();

    public static bool IsValid(string? backupId) => backupId is not null && Pattern.IsMatch(backupId);

    [GeneratedRegex(@"^\d{8}T\d{9}Z-\d{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex BackupIdRegex();
}
