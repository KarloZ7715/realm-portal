using System.Text.RegularExpressions;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.Clients;

/// <summary>
/// Reads the game folders referenced by a Lutris game configuration (<c>~/.config/lutris/games/*.yml</c>).
/// The file is read line by line in a bounded, non-following read; there is no YAML deserialization, so a
/// hostile or malformed configuration can only ever yield paths, never code or unlimited work.
/// </summary>
internal static partial class LutrisGameConfigs
{
    internal const int MaxBytes = 256 * 1024;

    private static readonly string[] PathKeys =
        ["exe", "prefix", "directory", "working_dir", "wine_prefix", "game_path", "path", "root"];

    private static readonly HashSet<string> SkippedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "cache", "caches", "node_modules", ".git", "steamapps",
    };

    /// <summary>Candidate client folders mentioned by the configuration; empty when the file cannot be used.</summary>
    internal static IReadOnlyList<string> ReadCandidateDirectories(string configPath)
    {
        if (!SafeFileRead.TryReadTextNonFollowing(configPath, MaxBytes, out var text, out _))
            return [];

        var directories = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] is '#' or '-')
                continue;

            var match = KeyValueLine().Match(line);
            if (!match.Success)
                continue;

            var key = match.Groups["key"].Value;
            if (!PathKeys.Contains(key, StringComparer.Ordinal))
                continue;

            var value = Unquote(match.Groups["value"].Value);
            if (value.Length == 0 || value[0] is '[' or '{' or '&' or '*')
                continue;

            // 'exe' points at the game executable (usually <client>/Wow.exe); the folder that holds it is the client.
            directories.Add(key == "exe" ? Path.GetDirectoryName(value) ?? string.Empty : value);
        }

        return directories
            .Where(dir => dir.Length > 0 && !HasSkippedSegment(dir))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool HasSkippedSegment(string path) =>
        path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .Any(segment => SkippedSegments.Contains(segment));

    /// <summary>Strips one layer of matching single or double quotes and expands a leading <c>~</c>.</summary>
    private static string Unquote(string raw)
    {
        var value = raw.Trim();
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
        {
            value = value[1..^1];
        }
        else
        {
            var comment = value.IndexOf(" #", StringComparison.Ordinal);
            if (comment > 0)
                value = value[..comment].TrimEnd();
        }

        if (value.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 0)
                value = home + value[1..];
        }

        return value;
    }

    [GeneratedRegex(@"^\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<value>.*?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueLine();
}
