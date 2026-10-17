namespace RealmPortal.App.Tests;

/// <summary>Finds files in the repository checkout (tests read the art and the theme sources from disk).</summary>
internal static class Repo
{
    public static string Path(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "RealmPortal.sln")))
        {
            directory = directory.Parent;
        }

        return System.IO.Path.Combine([directory?.FullName ?? throw new InvalidOperationException("RealmPortal.sln not found"), .. parts]);
    }
}
