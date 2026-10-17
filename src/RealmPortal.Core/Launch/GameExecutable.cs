namespace RealmPortal.Core.Launch;

/// <summary>Locates the client executable inside a client directory.</summary>
public static class GameExecutable
{
    public const string FileName = "Wow.exe";

    /// <summary>Returns the full path of <c>Wow.exe</c> (matched case-insensitively) directly inside the directory, or <c>null</c>.</summary>
    public static string? Find(string clientDirectory)
    {
        if (!Directory.Exists(clientDirectory))
            return null;

        return Directory.EnumerateFiles(clientDirectory)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase));
    }
}
