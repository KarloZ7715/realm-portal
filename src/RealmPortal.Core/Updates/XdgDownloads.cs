namespace RealmPortal.Core.Updates;

/// <summary>Resolves the user Downloads folder (XDG on Unix, known folder on Windows).</summary>
public static class XdgDownloads
{
    public static string GetDirectory(Func<string, string?>? getVariable = null)
    {
        var get = getVariable ?? Environment.GetEnvironmentVariable;
        var xdg = get("XDG_DOWNLOAD_DIR");
        if (!string.IsNullOrWhiteSpace(xdg))
            return Path.GetFullPath(xdg);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var known = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloads = Path.Combine(known, "Downloads");
            return Path.GetFullPath(downloads);
        }

        return Path.GetFullPath(Path.Combine(home, "Downloads"));
    }

    /// <summary>Picks a free path: <paramref name="fileName"/>, then <c> (1)</c>, <c> (2)</c>, … never overwriting.</summary>
    public static string AllocateNoncollidingPath(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Directory.CreateDirectory(directory);
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
            return candidate;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; i < 10_000; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new IOException("Could not allocate a unique download path.");
    }
}
