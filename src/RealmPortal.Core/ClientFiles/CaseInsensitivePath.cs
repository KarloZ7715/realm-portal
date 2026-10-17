namespace RealmPortal.Core.ClientFiles;

/// <summary>Resolves path segments case-insensitively against what exists on disk (needed on case-sensitive Linux).</summary>
internal static class CaseInsensitivePath
{
    /// <summary>
    /// Walks <paramref name="segments"/> (all directories except the last, which is a file) below <paramref name="root"/>. Every segment that exists keeps its real name
    /// (an exact-case match wins, otherwise the first match in ordinal order); segments below the first missing one
    /// keep the canonical spelling passed in.
    /// </summary>
    /// <returns>The real segments and whether the whole path exists.</returns>
    public static (string[] Segments, bool Exists) Resolve(string root, params string[] segments)
    {
        var real = new string[segments.Length];
        var current = root;
        var exists = true;
        for (var i = 0; i < segments.Length; i++)
        {
            var wanted = segments[i];
            real[i] = wanted;
            if (!exists)
                continue;

            var last = i == segments.Length - 1;
            var found = Find(current, wanted, last);
            if (found is null)
            {
                exists = false;
                continue;
            }

            real[i] = found;
            current = Path.Combine(current, found);
        }

        return (real, exists);
    }

    /// <summary>The real name of the sub-directory of <paramref name="directory"/> that matches <paramref name="wanted"/> case-insensitively, or <c>null</c>.</summary>
    public static string? FindDirectory(string directory, string wanted) => Find(directory, wanted, wantFile: false);

    private static string? Find(string directory, string wanted, bool wantFile)
    {
        var exact = Path.Combine(directory, wanted);
        if (wantFile ? File.Exists(exact) : Directory.Exists(exact))
            return wanted;

        var names = wantFile
            ? Directory.EnumerateFiles(directory)
            : Directory.EnumerateDirectories(directory);
        return names
            .Select(Path.GetFileName)
            .Where(n => n is not null && string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Real sub-directory names of <paramref name="directory"/> (sorted ordinally); empty when it cannot be listed.</summary>
    public static IReadOnlyList<string> ChildDirectories(string directory) =>
        Directory.EnumerateDirectories(directory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
}
